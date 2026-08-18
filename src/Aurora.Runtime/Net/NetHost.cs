using System.Net;

namespace Aurora.Runtime.Net;

/// <summary>Pacote de um tipo que o próprio <see cref="NetHost"/> não trata — estado das
/// entidades do cliente, e futuramente input e RPC. Passado por <c>ref</c> porque
/// <see cref="NetReader"/> é ref struct: ele aponta direto pro buffer de recepção, sem cópia.</summary>
public delegate void NetHostPacketHandler(NetPeer from, ref NetReader reader);

/// <summary>
/// Lado que hospeda a partida. Aceita joins até encher, mantém a lista de jogadores e
/// derruba quem parou de responder. O host é jogador também (id 0) — não existe processo
/// de servidor separado.
/// <para>Cuida do handshake e da presença. O estado das entidades trafega por cima disso, no
/// <see cref="NetSyncSystem"/>, via <see cref="PacketReceived"/> e <see cref="Broadcast"/>.</para>
/// </summary>
public sealed class NetHost : IDisposable
{
    private readonly INetTransport _transport;
    private readonly List<NetPeer> _peers = [];
    private readonly Dictionary<IPEndPoint, NetPeer> _peersByAddress = [];
    private readonly Dictionary<byte, NetReliableChannel> _channels = [];
    private readonly byte[] _receiveBuffer = new byte[NetProtocol.MaxPacketSize];

    private bool _disposed;

    /// <param name="transport">Socket já aberto, ou um <see cref="LoopbackNetwork"/> em teste.</param>
    /// <param name="hostName">Nome do jogador que hospeda.</param>
    /// <param name="maxPlayers">Total de jogadores na sala, host incluído.</param>
    public NetHost(INetTransport transport, string hostName = "Host", int maxPlayers = NetProtocol.MaxPlayersLimit)
    {
        _transport = transport;
        MaxPlayers = Math.Clamp(maxPlayers, 1, NetProtocol.MaxPlayersLimit);

        // O host ocupa uma vaga desde o começo: sem isso uma sala de 8 caberia 8 clientes
        // MAIS o host, e a contagem que a UI mostra não bateria com a realidade.
        Self = new NetPeer(NetProtocol.HostId, SanitizeName(hostName, "Host"), transport.LocalEndPoint);
        _peers.Add(Self);
    }

    /// <summary>Abre uma porta UDP e começa a hospedar.</summary>
    /// <param name="port">Porta que os outros jogadores vão digitar junto do IP.</param>
    public static NetHost Start(string hostName = "Host", int port = NetProtocol.DefaultPort,
        int maxPlayers = NetProtocol.MaxPlayersLimit)
        => new(new UdpNetTransport(port), hostName, maxPlayers);

    /// <summary>O jogador que hospeda. Sempre <see cref="NetProtocol.HostId"/>.</summary>
    public NetPeer Self { get; }

    /// <summary>Total de vagas, host incluído.</summary>
    public int MaxPlayers { get; }

    /// <summary>Todos na sala, host na posição 0.</summary>
    public IReadOnlyList<NetPeer> Peers => _peers;

    public int PlayerCount => _peers.Count;

    public bool IsFull => _peers.Count >= MaxPlayers;

    /// <summary>Porta onde o host escuta — é o que aparece na tela pros outros digitarem.</summary>
    public int Port => _transport.LocalEndPoint.Port;

    /// <summary>Identificador do jogo na busca por salas. Só quem declara o mesmo aparece na
    /// lista de quem está procurando.</summary>
    public string GameId { get; set; } = NetProtocol.DefaultGameId;

    /// <summary>
    /// Número sorteado uma vez por sessão que identifica ESTA sala. Existe porque um PC com
    /// várias placas de rede (Wi-Fi + cabo, ou com VPN, WSL e Docker instalados) responde à
    /// busca por cada uma delas, com um IP de origem diferente em cada resposta — sem um
    /// identificador, a mesma partida apareceria duas ou três vezes na lista do jogador.
    /// </summary>
    public uint RoomId { get; } = unchecked((uint)Random.Shared.Next(int.MinValue, int.MaxValue));

    /// <summary>Nome da sala mostrado na lista. Vazio vira o nome de quem hospeda.</summary>
    public string RoomName { get; set; } = "";

    /// <summary>Responde às buscas da rede local. Desligue pra hospedar uma partida que só
    /// entra quem souber o IP.</summary>
    public bool Discoverable { get; set; } = true;

    /// <summary>Segundos sem receber nada de um peer antes de considerá-lo desconectado.
    /// Precisa ser bem maior que o intervalo de keepalive do cliente, senão uma engasgada
    /// de rede derruba quem está jogando normalmente.</summary>
    public float PeerTimeout { get; set; } = 5f;

    /// <summary>Um jogador entrou (não dispara pro próprio host).</summary>
    public event Action<NetPeer>? PeerJoined;

    /// <summary>Um jogador saiu, por vontade própria ou por timeout.</summary>
    public event Action<NetPeer, NetDisconnectReason>? PeerLeft;

    /// <summary>Um join foi recusado. Só informativo — serve pra log e pra UI do host.</summary>
    public event Action<IPEndPoint, NetRejectReason>? JoinRejected;

    /// <summary>Chegou um pacote que não faz parte do handshake, vindo de um jogador já
    /// aceito. É por aqui que o <see cref="NetSyncSystem"/> recebe o estado dos clientes.</summary>
    public event NetHostPacketHandler? PacketReceived;

    /// <summary>
    /// Consome tudo que chegou e verifica timeouts. Chame uma vez por frame, antes da lógica
    /// do jogo, pra que o frame já enxergue quem entrou ou saiu.
    /// </summary>
    public void Update(float deltaTime)
    {
        if (_disposed) return;

        ReceiveAll();

        foreach (var channel in _channels.Values)
            channel.Update(deltaTime);

        CheckTimeouts(deltaTime);
    }

    private void ReceiveAll()
    {
        // Laço até esvaziar: um frame de 16 ms com 7 clientes mandando keepalive rende vários
        // pacotes, e tratar só um por frame acumularia atraso na fila do SO.
        while (_transport.TryReceive(_receiveBuffer, out int length, out var from))
        {
            var packet = _receiveBuffer.AsSpan(0, length);

            if (!NetReader.TryParse(packet, out var reader))
            {
                // Magic bate mas versão não: é um jogador com outro build, não lixo de rede.
                // Vale gastar um pacote avisando, senão a tela dele fica em "conectando..."
                // até o timeout, sem explicação nenhuma.
                if (NetReader.TryPeekVersion(packet, out byte version) && version != NetProtocol.Version)
                    Reject(from, NetRejectReason.VersionMismatch);

                continue;
            }

            Handle(ref reader, from);
        }
    }

    private void Handle(ref NetReader reader, IPEndPoint from)
    {
        _peersByAddress.TryGetValue(from, out var peer);

        if (peer is not null)
            peer.SilentFor = 0f;

        switch (reader.Type)
        {
            case NetMessageType.Join:
                HandleJoin(ref reader, from, peer);
                break;

            case NetMessageType.Ping:
                if (peer is not null)
                    SendTo(from, NetMessageType.Pong);
                break;

            case NetMessageType.Bye:
                if (peer is not null)
                    RemovePeer(peer, NetDisconnectReason.Requested);
                break;

            // Antes de qualquer coisa que dependa de peer: quem está procurando sala ainda não
            // entrou, e por definição vem de um endereço desconhecido.
            case NetMessageType.Discover:
                HandleDiscover(ref reader, from);
                break;

            case NetMessageType.Reliable:
                if (peer is not null)
                    ChannelFor(peer).OnReliable(ref reader);
                break;

            case NetMessageType.ReliableAck:
                if (peer is not null)
                    ChannelFor(peer).OnAck(ref reader);
                break;

            // Tipos de outras camadas (estado, input, RPC) vão pro assinante. Só de quem já
            // entrou: aceitar de endereço desconhecido deixaria qualquer um na rede mexer na
            // partida sem nem passar pelo join.
            default:
                if (peer is not null)
                    PacketReceived?.Invoke(peer, ref reader);
                break;
        }
    }

    private void HandleDiscover(ref NetReader reader, IPEndPoint from)
    {
        if (!Discoverable) return;
        if (!reader.TryReadString(out string gameId)) return;
        if (gameId != GameId) return;

        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.RoomInfo);
        writer.WriteString(GameId);
        writer.WriteUInt32(RoomId);
        writer.WriteString(string.IsNullOrWhiteSpace(RoomName) ? Self.Name : RoomName);
        writer.WriteString(Self.Name);
        writer.WriteByte((byte)PlayerCount);
        writer.WriteByte((byte)MaxPlayers);

        if (writer.Overflowed) return;

        // Responde mesmo com a sala cheia: melhor aparecer como "3/3 cheio" do que sumir da
        // lista e o jogador ficar achando que digitou o IP errado.
        _transport.Send(writer.Written, from);
    }

    private void HandleJoin(ref NetReader reader, IPEndPoint from, NetPeer? existing)
    {
        if (!reader.TryReadString(out string name)) return;

        // Join repetido do mesmo endereço quase sempre significa que o JoinAccepted se perdeu
        // e o cliente reenviou. Responder de novo com o mesmo id conserta; criar outro peer
        // duplicaria o jogador na sala e queimaria uma vaga.
        if (existing is not null)
        {
            SendJoinAccepted(existing);
            return;
        }

        if (IsFull)
        {
            Reject(from, NetRejectReason.Full);
            return;
        }

        var peer = new NetPeer(NextFreeId(), SanitizeName(name, "Jogador"), from);
        _peers.Add(peer);
        _peersByAddress[from] = peer;

        SendJoinAccepted(peer);
        BroadcastPeerJoined(peer);

        PeerJoined?.Invoke(peer);
    }

    /// <summary>Menor id livre. Reaproveitar ids de quem saiu mantém todo mundo dentro de
    /// 0..MaxPlayers-1, o que deixa o id caber num byte e servir de índice direto nas
    /// tabelas de estado por jogador da fase 2.</summary>
    private byte NextFreeId()
    {
        for (byte id = 1; id < NetProtocol.MaxPlayersLimit; id++)
        {
            bool taken = false;
            foreach (var peer in _peers)
            {
                if (peer.Id != id) continue;

                taken = true;
                break;
            }

            if (!taken) return id;
        }

        throw new InvalidOperationException("Sem id livre — NextFreeId chamado com a sala cheia.");
    }

    private void CheckTimeouts(float deltaTime)
    {
        // De trás pra frente porque RemovePeer tira da mesma lista que estamos percorrendo.
        // Começa em 1: o host (índice 0) não manda pacote pra si mesmo e expiraria sozinho.
        for (int i = _peers.Count - 1; i >= 1; i--)
        {
            var peer = _peers[i];
            peer.SilentFor += deltaTime;

            if (peer.SilentFor >= PeerTimeout)
                RemovePeer(peer, NetDisconnectReason.TimedOut);
        }
    }

    private void RemovePeer(NetPeer peer, NetDisconnectReason reason)
    {
        _peers.Remove(peer);
        _peersByAddress.Remove(peer.Address);

        // Ids são reaproveitados: um canal esquecido faria o próximo jogador a receber esse id
        // começar com a numeração da sessão anterior e nunca ter mensagem nenhuma entregue.
        _channels.Remove(peer.Id);

        BroadcastPeerLeft(peer);
        PeerLeft?.Invoke(peer, reason);
    }

    /// <summary>Avisa todo mundo que a partida acabou e esvazia a sala. Sem isso os clientes
    /// só descobrem pelo timeout, segundos depois, olhando pra uma tela congelada.</summary>
    public void Shutdown()
    {
        if (_disposed) return;

        foreach (var peer in _peers)
        {
            if (peer.IsHost) continue;
            SendTo(peer.Address, NetMessageType.Bye);
        }

        _peers.RemoveAll(p => !p.IsHost);
        _peersByAddress.Clear();
        _channels.Clear();
    }

    private void Reject(IPEndPoint to, NetRejectReason reason)
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.HeaderSize + 1];
        var writer = new NetWriter(buffer, NetMessageType.JoinRejected);
        writer.WriteByte((byte)reason);

        _transport.Send(writer.Written, to);
        JoinRejected?.Invoke(to, reason);
    }

    private void SendJoinAccepted(NetPeer target)
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.JoinAccepted);
        writer.WriteByte(target.Id);
        writer.WriteByte((byte)MaxPlayers);
        writer.WriteByte((byte)_peers.Count);

        // Lista completa, incluindo o host e o próprio recém-chegado: o cliente monta a sala
        // inteira com esse único pacote, sem depender de ter visto PeerJoined anteriores.
        foreach (var peer in _peers)
        {
            writer.WriteByte(peer.Id);
            writer.WriteString(peer.Name);
        }

        _transport.Send(writer.Written, target.Address);
    }

    private void BroadcastPeerJoined(NetPeer joined)
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.PeerJoined);
        writer.WriteByte(joined.Id);
        writer.WriteString(joined.Name);

        Broadcast(writer.Written, except: joined);
    }

    private void BroadcastPeerLeft(NetPeer left)
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.HeaderSize + 1];
        var writer = new NetWriter(buffer, NetMessageType.PeerLeft);
        writer.WriteByte(left.Id);

        Broadcast(writer.Written, except: left);
    }

    /// <summary>Manda um pacote já montado pra todos os clientes.</summary>
    public void Broadcast(ReadOnlySpan<byte> packet) => Broadcast(packet, except: null);

    /// <summary>Como <see cref="SendTo"/>, mas com entrega garantida e em ordem. Use pra
    /// evento que não pode se perder; posição de entidade não precisa e ficaria mais cara.</summary>
    public void SendReliableTo(NetPeer peer, ReadOnlySpan<byte> packet)
    {
        if (peer.IsHost) return;
        ChannelFor(peer).Send(packet);
    }

    /// <summary>Como <see cref="Broadcast"/>, mas com entrega garantida e em ordem.</summary>
    public void BroadcastReliable(ReadOnlySpan<byte> packet, NetPeer? except = null)
    {
        foreach (var peer in _peers)
        {
            if (peer.IsHost || ReferenceEquals(peer, except)) continue;
            ChannelFor(peer).Send(packet);
        }
    }

    /// <summary>Canal confiável daquele jogador, criado na primeira necessidade.</summary>
    private NetReliableChannel ChannelFor(NetPeer peer)
    {
        if (_channels.TryGetValue(peer.Id, out var channel)) return channel;

        var address = peer.Address;
        channel = new NetReliableChannel
        {
            Transmit = data => _transport.Send(data.Span, address),
        };

        // O pacote entregue volta pro mesmo caminho de um pacote comum: quem assina
        // PacketReceived não precisa saber se veio pelo canal confiável ou solto.
        channel.Delivered += packet => HandleDelivered(peer, packet);

        _channels[peer.Id] = channel;
        return channel;
    }

    private void HandleDelivered(NetPeer peer, byte[] packet)
    {
        if (!NetReader.TryParse(packet, out var reader)) return;

        // Envelope dentro de envelope não existe no protocolo; recusar aqui fecha o laço
        // infinito que um pacote forjado poderia provocar.
        if (reader.Type is NetMessageType.Reliable or NetMessageType.ReliableAck) return;

        PacketReceived?.Invoke(peer, ref reader);
    }

    /// <summary>Manda um pacote já montado pra um cliente só.</summary>
    public void SendTo(NetPeer peer, ReadOnlySpan<byte> packet)
    {
        if (peer.IsHost) return;
        _transport.Send(packet, peer.Address);
    }

    private void Broadcast(ReadOnlySpan<byte> packet, NetPeer? except = null)
    {
        foreach (var peer in _peers)
        {
            if (peer.IsHost || ReferenceEquals(peer, except)) continue;
            _transport.Send(packet, peer.Address);
        }
    }

    private void SendTo(IPEndPoint to, NetMessageType type)
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.HeaderSize];
        var writer = new NetWriter(buffer, type);

        _transport.Send(writer.Written, to);
    }

    private static string SanitizeName(string name, string fallback)
    {
        name = name.Trim();
        if (name.Length == 0) return fallback;

        return name.Length > NetProtocol.MaxNameLength ? name[..NetProtocol.MaxNameLength] : name;
    }

    public void Dispose()
    {
        if (_disposed) return;

        Shutdown();
        _disposed = true;
        _transport.Dispose();
    }
}
