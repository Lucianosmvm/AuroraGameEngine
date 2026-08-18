using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Aurora.Runtime.Net;

/// <summary>
/// Procura partidas na rede local, pra ninguém precisar digitar IP.
///
/// <para>Funciona por pergunta e resposta: o navegador manda um pacote de broadcast, e todo
/// host daquele jogo que ouvir responde com o nome da sala e quantos jogadores tem. O caminho
/// contrário (host anunciando sozinho o tempo todo) gastaria rede de graça enquanto ninguém
/// está procurando.</para>
///
/// <para>A pergunta vai na mesma porta do jogo: um segundo socket seria mais uma porta pro
/// jogador liberar no firewall sem ganhar nada.</para>
/// </summary>
public sealed class NetBrowser : IDisposable
{
    private readonly INetTransport _transport;
    private readonly List<NetRoomInfo> _rooms = [];
    private readonly byte[] _receiveBuffer = new byte[NetProtocol.MaxPacketSize];

    private float _sinceProbe;
    private bool _disposed;

    /// <param name="transport">Socket próprio do navegador (porta qualquer).</param>
    /// <param name="gameId">Só responde host com o mesmo identificador. Sem isso, dois jogos
    /// Aurora diferentes na mesma rede apareceriam um na lista do outro.</param>
    /// <param name="hostPort">Porta onde os hosts escutam.</param>
    public NetBrowser(INetTransport transport, string gameId, int hostPort = NetProtocol.DefaultPort)
    {
        _transport = transport;
        GameId = gameId;
        HostPort = hostPort;
        BroadcastTargets = DefaultBroadcastTargets(hostPort);
    }

    /// <summary>Abre um socket próprio e começa a poder procurar.</summary>
    public static NetBrowser Create(string gameId, int hostPort = NetProtocol.DefaultPort)
        => new(new UdpNetTransport(0), gameId, hostPort);

    public string GameId { get; }

    public int HostPort { get; }

    /// <summary>Pra onde as perguntas vão. Por padrão, o broadcast geral mais o de cada placa
    /// de rede — Windows com várias interfaces (Wi-Fi + Ethernet + VPN + adaptador do Docker)
    /// nem sempre entrega o 255.255.255.255 por todas elas.</summary>
    public IReadOnlyList<IPEndPoint> BroadcastTargets { get; set; }

    /// <summary>Salas encontradas, da mais recente resposta pra mais antiga descoberta.</summary>
    public IReadOnlyList<NetRoomInfo> Rooms => _rooms;

    /// <summary>Tempo entre perguntas automáticas.</summary>
    public float ProbeInterval { get; set; } = 1f;

    /// <summary>Tempo sem resposta antes de tirar a sala da lista. Precisa ser alguns
    /// intervalos de pergunta, senão uma resposta perdida faz a sala piscar na tela.</summary>
    public float RoomTimeout { get; set; } = 3f;

    /// <summary>A lista mudou (sala nova, sala que sumiu, contagem de jogadores diferente).</summary>
    public event Action? RoomsChanged;

    /// <summary>Pergunta agora, sem esperar o intervalo. Use no botão "atualizar".</summary>
    public void Refresh()
    {
        _sinceProbe = 0f;
        SendProbe();
    }

    /// <summary>
    /// Pergunta direto a um endereço, sem broadcast. É a saída pra rede que bloqueia broadcast
    /// (comum em Wi-Fi de empresa e em alguns roteadores com isolamento de cliente): o jogador
    /// digita o IP e a sala aparece na lista igual às outras, já com nome e lotação.
    /// </summary>
    public void Probe(string address, int? port = null)
    {
        if (!TryResolve(address, port ?? HostPort, out var endpoint)) return;

        SendProbeTo(endpoint);
    }

    /// <summary>Pergunta de tempos em tempos e envelhece a lista. Chame uma vez por frame
    /// enquanto a tela de procurar partida estiver aberta.</summary>
    public void Update(float deltaTime)
    {
        if (_disposed) return;

        ReceiveAll();

        _sinceProbe += deltaTime;
        if (_sinceProbe >= ProbeInterval)
        {
            _sinceProbe = 0f;
            SendProbe();
        }

        Expire(deltaTime);
    }

    /// <summary>Esvazia a lista. Use ao abrir a tela, pra não mostrar sala de uma busca antiga.</summary>
    public void Clear()
    {
        if (_rooms.Count == 0) return;

        _rooms.Clear();
        RoomsChanged?.Invoke();
    }

    private void ReceiveAll()
    {
        while (_transport.TryReceive(_receiveBuffer, out int length, out var from))
        {
            if (!NetReader.TryParse(_receiveBuffer.AsSpan(0, length), out var reader)) continue;
            if (reader.Type != NetMessageType.RoomInfo) continue;

            if (!reader.TryReadString(out string gameId)) continue;
            if (!reader.TryReadUInt32(out uint roomId)) continue;
            if (!reader.TryReadString(out string roomName)) continue;
            if (!reader.TryReadString(out string hostName)) continue;
            if (!reader.TryReadByte(out byte playerCount)) continue;
            if (!reader.TryReadByte(out byte maxPlayers)) continue;

            // Outro jogo Aurora na mesma rede respondeu por acaso.
            if (gameId != GameId) continue;

            Merge(roomId, from, roomName, hostName, playerCount, maxPlayers);
        }
    }

    private void Merge(uint roomId, IPEndPoint address, string roomName, string hostName,
        byte playerCount, byte maxPlayers)
    {
        foreach (var room in _rooms)
        {
            // Casa pelo identificador da sala, não pelo endereço: o mesmo host responde por
            // cada placa de rede que tiver, com origem diferente em cada resposta. O endereço
            // guardado é o da primeira que chegou — ela comprovadamente tem caminho de volta.
            if (room.RoomId != roomId) continue;

            bool changed = room.RoomName != roomName
                || room.HostName != hostName
                || room.PlayerCount != playerCount
                || room.MaxPlayers != maxPlayers;

            room.RoomName = roomName;
            room.HostName = hostName;
            room.PlayerCount = playerCount;
            room.MaxPlayers = maxPlayers;
            room.SilentFor = 0f;

            if (changed) RoomsChanged?.Invoke();
            return;
        }

        _rooms.Add(new NetRoomInfo(roomId, address, roomName, hostName, playerCount, maxPlayers));
        RoomsChanged?.Invoke();
    }

    private void Expire(float deltaTime)
    {
        bool removed = false;

        for (int i = _rooms.Count - 1; i >= 0; i--)
        {
            _rooms[i].SilentFor += deltaTime;
            if (_rooms[i].SilentFor < RoomTimeout) continue;

            _rooms.RemoveAt(i);
            removed = true;
        }

        if (removed) RoomsChanged?.Invoke();
    }

    private void SendProbe()
    {
        foreach (var target in BroadcastTargets)
            SendProbeTo(target);
    }

    private void SendProbeTo(IPEndPoint target)
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Discover);
        writer.WriteString(GameId);

        if (writer.Overflowed) return;

        _transport.Send(writer.Written, target);
    }

    private static bool TryResolve(string address, int port, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.Loopback, port);

        if (IPAddress.TryParse(address, out var parsed))
        {
            endpoint = new IPEndPoint(parsed, port);
            return true;
        }

        try
        {
            if (Dns.GetHostAddresses(address, AddressFamily.InterNetwork).FirstOrDefault() is not { } resolved)
                return false;

            endpoint = new IPEndPoint(resolved, port);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>255.255.255.255 mais o endereço de broadcast de cada placa ativa. Repetido é
    /// inofensivo (o host responde a cada pergunta), e cobrir todas as placas é o que faz a
    /// busca funcionar em máquina com Wi-Fi e cabo ligados ao mesmo tempo.</summary>
    private static List<IPEndPoint> DefaultBroadcastTargets(int port)
    {
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, port) };

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (unicast.IPv4Mask is not { } mask) continue;

                    if (TryBroadcastOf(unicast.Address, mask) is { } broadcast)
                        targets.Add(new IPEndPoint(broadcast, port));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Sem permissão ou plataforma que não expõe as interfaces: o 255.255.255.255
            // sozinho já resolve na maioria das redes domésticas.
        }

        return targets;
    }

    private static IPAddress? TryBroadcastOf(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        if (addressBytes.Length != 4 || maskBytes.Length != 4) return null;

        var broadcast = new byte[4];
        for (int i = 0; i < 4; i++)
            broadcast[i] = (byte)(addressBytes[i] | (byte)~maskBytes[i]);

        return new IPAddress(broadcast);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _transport.Dispose();
    }
}
