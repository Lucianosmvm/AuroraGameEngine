using System.Net;

namespace Aurora.Runtime.Net;

/// <summary>Em que ponto da conexão o cliente está.</summary>
public enum NetClientState
{
    /// <summary>Nem tentou, ou já saiu.</summary>
    Disconnected = 0,

    /// <summary>Join enviado, esperando resposta do host.</summary>
    Connecting = 1,

    /// <summary>Dentro da sala.</summary>
    Connected = 2,
}

/// <summary>Pacote de um tipo que o próprio <see cref="NetClient"/> não trata — snapshot de
/// estado, e futuramente RPC. Ver <see cref="NetHostPacketHandler"/> quanto ao <c>ref</c>.</summary>
public delegate void NetClientPacketHandler(ref NetReader reader);

/// <summary>
/// Lado que entra numa partida hospedada por outro jogador, informando IP e porta.
/// <para>Cuida do handshake, da lista de jogadores e do keepalive. O estado das entidades
/// trafega por cima disso, no <see cref="NetSyncSystem"/>.</para>
/// </summary>
public sealed class NetClient : IDisposable
{
    private readonly INetTransport _transport;
    private readonly byte[] _receiveBuffer = new byte[NetProtocol.MaxPacketSize];
    private readonly List<NetPeer> _peers = [];
    private readonly NetReliableChannel _channel;

    private IPEndPoint? _hostAddress;
    private string _playerName = "Jogador";
    private float _sinceJoinAttempt;
    private float _connectingFor;
    private float _sinceKeepAlive;
    private float _hostSilentFor;
    private bool _disposed;

    /// <param name="transport">Socket já aberto, ou um <see cref="LoopbackNetwork"/> em teste.</param>
    public NetClient(INetTransport transport)
    {
        _transport = transport;

        _channel = new NetReliableChannel
        {
            Transmit = data =>
            {
                if (_hostAddress is not null)
                    _transport.Send(data.Span, _hostAddress);
            },
        };

        // O pacote entregue volta pro mesmo caminho de um pacote comum: quem assina
        // PacketReceived não precisa saber por onde ele veio.
        _channel.Delivered += HandleDelivered;
    }

    /// <summary>Abre uma porta local qualquer (o SO escolhe) pra falar com o host.</summary>
    public static NetClient Create() => new(new UdpNetTransport(0));

    public NetClientState State { get; private set; } = NetClientState.Disconnected;

    /// <summary>Porta local deste cliente. Escolhida pelo SO — só útil pra log e pra teste.</summary>
    public int LocalPort => _transport.LocalEndPoint.Port;

    /// <summary>Id recebido do host. Só tem valor quando <see cref="State"/> é
    /// <see cref="NetClientState.Connected"/>.</summary>
    public byte SelfId { get; private set; }

    /// <summary>Todos na sala, host incluído — a mesma lista que o host enxerga.</summary>
    public IReadOnlyList<NetPeer> Peers => _peers;

    /// <summary>Vagas totais da sala, informado pelo host no aceite.</summary>
    public int MaxPlayers { get; private set; }

    /// <summary>Motivo da última recusa de join, quando houve.</summary>
    public NetRejectReason LastRejectReason { get; private set; }

    /// <summary>Tempo total tentando conectar antes de desistir, em segundos.</summary>
    public float ConnectTimeout { get; set; } = 5f;

    /// <summary>Intervalo entre reenvios do Join enquanto não vem resposta. UDP perde pacote
    /// sem avisar, e o Join é o único que não pode simplesmente ser perdido — sem reenvio,
    /// um pacote sumido significa "não consegui entrar" sem nenhum erro real por trás.</summary>
    public float JoinRetryInterval { get; set; } = 0.25f;

    /// <summary>Intervalo do keepalive depois de conectado. Precisa ser bem menor que o
    /// <see cref="NetHost.PeerTimeout"/> do host, senão um jogador parado (sem apertar nada)
    /// seria expulso por engano.</summary>
    public float KeepAliveInterval { get; set; } = 1f;

    /// <summary>Segundos sem notícia do host antes de considerar a partida caída.</summary>
    public float HostTimeout { get; set; } = 5f;

    /// <summary>Entrou na sala. Traz o id recebido.</summary>
    public event Action<byte>? Connected;

    /// <summary>Saiu, caiu ou foi recusado.</summary>
    public event Action<NetDisconnectReason>? Disconnected;

    /// <summary>Outro jogador entrou.</summary>
    public event Action<NetPeer>? PeerJoined;

    /// <summary>Outro jogador saiu.</summary>
    public event Action<NetPeer>? PeerLeft;

    /// <summary>Chegou um pacote do host que não faz parte do handshake. É por aqui que o
    /// <see cref="NetSyncSystem"/> recebe os snapshots.</summary>
    public event NetClientPacketHandler? PacketReceived;

    /// <summary>Começa a conectar. Não bloqueia: o resultado chega em
    /// <see cref="Connected"/> ou <see cref="Disconnected"/> ao longo dos próximos frames.</summary>
    /// <param name="host">IP ou hostname que o jogador digitou.</param>
    public void Connect(string host, int port = NetProtocol.DefaultPort, string playerName = "Jogador")
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            // Nome em vez de IP ("pc-da-sala"): resolver falha se o DNS/mDNS local não conhecer
            // o nome, e aí é erro de digitação do jogador, não falha de rede.
            try
            {
                address = Dns.GetHostAddresses(host, System.Net.Sockets.AddressFamily.InterNetwork).FirstOrDefault()
                    ?? throw new System.Net.Sockets.SocketException();
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
            {
                Fail(NetDisconnectReason.ConnectFailed);
                return;
            }
        }

        Connect(new IPEndPoint(address, port), playerName);
    }

    /// <summary>Começa a conectar num endereço já resolvido.</summary>
    public void Connect(IPEndPoint host, string playerName = "Jogador")
    {
        if (_disposed) return;

        _hostAddress = host;
        _playerName = playerName;
        _peers.Clear();
        SelfId = 0;
        MaxPlayers = 0;
        LastRejectReason = NetRejectReason.Unknown;
        _connectingFor = 0f;
        _sinceKeepAlive = 0f;
        _hostSilentFor = 0f;
        _channel.Reset();
        State = NetClientState.Connecting;

        // Primeiro Join sai já, sem esperar o intervalo — numa LAN a resposta costuma voltar
        // no mesmo frame, e adiar isso só adicionaria latência visível ao entrar.
        _sinceJoinAttempt = JoinRetryInterval;
        SendJoin();
    }

    /// <summary>Consome o que chegou, reenvia Join se preciso e mantém o keepalive.
    /// Chame uma vez por frame.</summary>
    public void Update(float deltaTime)
    {
        if (_disposed || State == NetClientState.Disconnected) return;

        ReceiveAll();
        _channel.Update(deltaTime);

        // O estado pode ter mudado dentro do Receive (aceite, recusa, Bye do host); reavalia
        // antes de mexer nos temporizadores, senão um Bye recebido agora ainda geraria um Ping.
        switch (State)
        {
            case NetClientState.Connecting:
                UpdateConnecting(deltaTime);
                break;

            case NetClientState.Connected:
                UpdateConnected(deltaTime);
                break;
        }
    }

    private void UpdateConnecting(float deltaTime)
    {
        _connectingFor += deltaTime;
        _sinceJoinAttempt += deltaTime;

        if (_connectingFor >= ConnectTimeout)
        {
            Fail(NetDisconnectReason.ConnectFailed);
            return;
        }

        if (_sinceJoinAttempt < JoinRetryInterval) return;

        _sinceJoinAttempt = 0f;
        SendJoin();
    }

    private void UpdateConnected(float deltaTime)
    {
        _sinceKeepAlive += deltaTime;
        _hostSilentFor += deltaTime;

        if (_hostSilentFor >= HostTimeout)
        {
            Fail(NetDisconnectReason.TimedOut);
            return;
        }

        if (_sinceKeepAlive < KeepAliveInterval) return;

        _sinceKeepAlive = 0f;
        SendTo(NetMessageType.Ping);
    }

    private void ReceiveAll()
    {
        while (_transport.TryReceive(_receiveBuffer, out int length, out var from))
        {
            // Só o host tem o que dizer. Descartar o resto evita que qualquer um na rede
            // mande um PeerLeft forjado e bagunce a lista de jogadores.
            if (!from.Equals(_hostAddress)) continue;
            if (!NetReader.TryParse(_receiveBuffer.AsSpan(0, length), out var reader)) continue;

            _hostSilentFor = 0f;
            Handle(ref reader);
        }
    }

    private void Handle(ref NetReader reader)
    {
        switch (reader.Type)
        {
            case NetMessageType.JoinAccepted:
                HandleJoinAccepted(ref reader);
                break;

            case NetMessageType.JoinRejected:
                HandleJoinRejected(ref reader);
                break;

            case NetMessageType.PeerJoined:
                HandlePeerJoined(ref reader);
                break;

            case NetMessageType.PeerLeft:
                HandlePeerLeft(ref reader);
                break;

            case NetMessageType.Ping:
                SendTo(NetMessageType.Pong);
                break;

            case NetMessageType.Bye:
                Fail(NetDisconnectReason.HostShutdown);
                break;

            case NetMessageType.Reliable:
                _channel.OnReliable(ref reader);
                break;

            case NetMessageType.ReliableAck:
                _channel.OnAck(ref reader);
                break;

            // Pong não precisa de tratamento: o efeito dele é o _hostSilentFor zerado acima.
            // O resto (estado, RPC) vai pro assinante.
            default:
                PacketReceived?.Invoke(ref reader);
                break;
        }
    }

    private void HandleJoinAccepted(ref NetReader reader)
    {
        if (!reader.TryReadByte(out byte selfId)) return;
        if (!reader.TryReadByte(out byte maxPlayers)) return;
        if (!reader.TryReadByte(out byte peerCount)) return;

        // Monta numa lista à parte e só troca no fim: um pacote truncado no meio da lista
        // deixaria a sala meio preenchida se escrevêssemos direto em _peers.
        var roster = new List<NetPeer>(peerCount);
        for (int i = 0; i < peerCount; i++)
        {
            if (!reader.TryReadByte(out byte id)) return;
            if (!reader.TryReadString(out string name)) return;

            roster.Add(new NetPeer(id, name, AddressOf(id)));
        }

        _peers.Clear();
        _peers.AddRange(roster);

        SelfId = selfId;
        MaxPlayers = maxPlayers;

        // JoinAccepted repetido acontece quando o host não viu nosso primeiro reenvio parar.
        // A lista é atualizada de novo (é sempre a verdade mais recente), mas o evento de
        // conexão só dispara uma vez.
        if (State == NetClientState.Connected) return;

        State = NetClientState.Connected;
        _sinceKeepAlive = 0f;
        _hostSilentFor = 0f;
        Connected?.Invoke(selfId);
    }

    private void HandleJoinRejected(ref NetReader reader)
    {
        LastRejectReason = reader.TryReadByte(out byte reason)
            ? (NetRejectReason)reason
            : NetRejectReason.Unknown;

        Fail(NetDisconnectReason.Rejected);
    }

    private void HandlePeerJoined(ref NetReader reader)
    {
        if (!reader.TryReadByte(out byte id)) return;
        if (!reader.TryReadString(out string name)) return;
        if (FindPeer(id) is not null) return;

        var peer = new NetPeer(id, name, AddressOf(id));
        _peers.Add(peer);
        PeerJoined?.Invoke(peer);
    }

    private void HandlePeerLeft(ref NetReader reader)
    {
        if (!reader.TryReadByte(out byte id)) return;
        if (FindPeer(id) is not { } peer) return;

        _peers.Remove(peer);
        PeerLeft?.Invoke(peer);
    }

    private NetPeer? FindPeer(byte id)
    {
        foreach (var peer in _peers)
        {
            if (peer.Id == id) return peer;
        }

        return null;
    }

    /// <summary>No cliente só o host tem endereço conhecido — ninguém fala direto com os
    /// outros jogadores, tudo passa pelo host.</summary>
    private IPEndPoint AddressOf(byte id)
        => id == NetProtocol.HostId && _hostAddress is not null ? _hostAddress : NetPeer.UnknownAddress;

    /// <summary>Sai da sala avisando o host. Sem o aviso ele só percebe pelo timeout, e o
    /// boneco fica parado na tela dos outros até lá.</summary>
    public void Disconnect()
    {
        if (State == NetClientState.Disconnected) return;

        SendTo(NetMessageType.Bye);
        Fail(NetDisconnectReason.Requested);
    }

    private void Fail(NetDisconnectReason reason)
    {
        State = NetClientState.Disconnected;
        _peers.Clear();
        Disconnected?.Invoke(reason);
    }

    private void SendJoin()
    {
        if (_hostAddress is null) return;

        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Join);
        writer.WriteString(_playerName);

        _transport.Send(writer.Written, _hostAddress);
    }

    /// <summary>Manda um pacote já montado pro host.</summary>
    public void Send(ReadOnlySpan<byte> packet)
    {
        if (_hostAddress is null || State != NetClientState.Connected) return;
        _transport.Send(packet, _hostAddress);
    }

    /// <summary>Como <see cref="Send"/>, mas com entrega garantida e em ordem. Use pra evento
    /// que não pode se perder; posição de entidade não precisa e ficaria mais cara.</summary>
    public void SendReliable(ReadOnlySpan<byte> packet)
    {
        if (_hostAddress is null || State != NetClientState.Connected) return;
        _channel.Send(packet);
    }

    private void HandleDelivered(byte[] packet)
    {
        if (!NetReader.TryParse(packet, out var reader)) return;

        // Envelope dentro de envelope não existe no protocolo; recusar aqui fecha o laço
        // infinito que um pacote forjado poderia provocar.
        if (reader.Type is NetMessageType.Reliable or NetMessageType.ReliableAck) return;

        PacketReceived?.Invoke(ref reader);
    }

    private void SendTo(NetMessageType type)
    {
        if (_hostAddress is null) return;

        Span<byte> buffer = stackalloc byte[NetProtocol.HeaderSize];
        var writer = new NetWriter(buffer, type);

        _transport.Send(writer.Written, _hostAddress);
    }

    public void Dispose()
    {
        if (_disposed) return;

        Disconnect();
        _disposed = true;
        _transport.Dispose();
    }
}
