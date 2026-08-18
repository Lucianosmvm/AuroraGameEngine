using Aurora.Runtime.Ecs;

namespace Aurora.Runtime.Net;

/// <summary>Papel desta máquina na partida.</summary>
public enum NetRole
{
    /// <summary>Jogo offline — nenhum socket aberto.</summary>
    Offline = 0,

    /// <summary>Esta máquina hospeda.</summary>
    Host = 1,

    /// <summary>Esta máquina entrou na sala de outro jogador.</summary>
    Client = 2,
}

/// <summary>
/// Ponto único de acesso à rede a partir do jogo: esconde a diferença entre
/// <see cref="NetHost"/> e <see cref="NetClient"/> atrás de uma lista de jogadores e um
/// punhado de eventos, pra que a lógica do jogo não precise de dois caminhos de código.
/// Vive em <c>Game.Net</c> e é atualizada uma vez por frame.
/// <para>Offline por padrão: quem nunca chama <see cref="StartHost"/> nem <see cref="Join"/>
/// não abre porta nenhuma e não paga nada por isso.</para>
/// </summary>
public sealed class NetSession : IDisposable
{
    private static readonly NetPeer[] Empty = [];

    private NetHost? _host;
    private NetClient? _client;

    public NetRole Role { get; private set; } = NetRole.Offline;

    /// <summary>Host cru, quando esta máquina hospeda. Null caso contrário.</summary>
    public NetHost? Host => _host;

    /// <summary>Cliente cru, quando esta máquina entrou numa sala. Null caso contrário.</summary>
    public NetClient? Client => _client;

    /// <summary>Sincronização de entidades. Null até <see cref="AttachWorld"/> ser chamado —
    /// <c>Game</c> faz isso no load, então em jogo é sempre não-nulo.</summary>
    public NetSyncSystem? Sync { get; private set; }

    /// <summary>Eventos nomeados entre as máquinas. Disponível sempre, inclusive offline —
    /// sem sala, a chamada é entregue localmente.</summary>
    public NetRpcSystem Rpc { get; }

    /// <summary>Busca de salas na rede local. Null até <see cref="StartBrowsing(int)"/>.</summary>
    public NetBrowser? Browser { get; private set; }

    /// <summary>
    /// Identificador do jogo na busca por salas. <c>Game</c> preenche com o <c>GameName</c>, e
    /// só aparecem na lista os hosts que declararam o mesmo — sem isso, dois jogos Aurora
    /// diferentes na mesma rede apareceriam um na lista do outro.
    /// </summary>
    public string GameId { get; set; } = NetProtocol.DefaultGameId;

    /// <summary>Nome da sala mostrado a quem está procurando. Vazio vira o nome de quem
    /// hospeda. Defina antes de <see cref="StartHost(string, int, int)"/>.</summary>
    public string RoomName { get; set; } = "";

    /// <summary>Salas encontradas. Vazia quando não está procurando.</summary>
    public IReadOnlyList<NetRoomInfo> Rooms => Browser?.Rooms ?? [];

    public NetSession()
    {
        Rpc = new NetRpcSystem(this);
    }

    public bool IsHost => Role == NetRole.Host;

    public bool IsOffline => Role == NetRole.Offline;

    /// <summary>True quando a sala está de pé e este jogador dentro dela. Um cliente ainda
    /// no meio do handshake conta como não pronto.</summary>
    public bool IsReady => Role switch
    {
        NetRole.Host => true,
        NetRole.Client => _client!.State == NetClientState.Connected,
        _ => false,
    };

    /// <summary>Id deste jogador na sala. 0 no host; no cliente, o número dado pelo host.</summary>
    public byte SelfId => Role switch
    {
        NetRole.Host => NetProtocol.HostId,
        NetRole.Client => _client!.SelfId,
        _ => NetProtocol.HostId,
    };

    /// <summary>Todos na sala, host incluído. Vazia quando offline.</summary>
    public IReadOnlyList<NetPeer> Peers => Role switch
    {
        NetRole.Host => _host!.Peers,
        NetRole.Client => _client!.Peers,
        _ => Empty,
    };

    public int PlayerCount => Peers.Count;

    /// <summary>Um jogador entrou na sala (dispara nos dois papéis).</summary>
    public event Action<NetPeer>? PlayerJoined;

    /// <summary>Um jogador saiu da sala (dispara nos dois papéis).</summary>
    public event Action<NetPeer>? PlayerLeft;

    /// <summary>Só no cliente: entrou na sala com sucesso.</summary>
    public event Action<byte>? JoinedRoom;

    /// <summary>Só no cliente: saiu, caiu ou foi recusado.</summary>
    public event Action<NetDisconnectReason>? LeftRoom;

    /// <summary>
    /// Passa a hospedar. Encerra qualquer sessão anterior.
    /// </summary>
    /// <param name="playerName">Nome deste jogador.</param>
    /// <param name="port">Porta a abrir — é o número que os outros digitam junto do IP.</param>
    /// <param name="maxPlayers">Vagas totais, este jogador incluído.</param>
    public NetHost StartHost(string playerName = "Host", int port = NetProtocol.DefaultPort,
        int maxPlayers = NetProtocol.MaxPlayersLimit)
        => StartHost(NetHost.Start(playerName, port, maxPlayers));

    /// <summary>Assume um host já construído — usado com <see cref="LoopbackNetwork"/> em teste.</summary>
    public NetHost StartHost(NetHost host)
    {
        Leave();

        // Hospedando, a busca não serve mais e o socket dela só ficaria mandando broadcast
        // de graça no meio da partida.
        StopBrowsing();

        _host = host;
        _host.GameId = GameId;
        _host.RoomName = RoomName;
        _host.PeerJoined += OnHostPeerJoined;
        _host.PeerLeft += OnHostPeerLeft;
        Rpc.Attach(host);
        Role = NetRole.Host;
        return host;
    }

    /// <summary>
    /// Entra na sala de outro jogador. Não bloqueia — acompanhe por <see cref="JoinedRoom"/>
    /// e <see cref="LeftRoom"/>.
    /// </summary>
    public NetClient Join(string hostAddress, int port = NetProtocol.DefaultPort,
        string playerName = "Jogador")
    {
        var client = Join(NetClient.Create());
        client.Connect(hostAddress, port, playerName);
        return client;
    }

    /// <summary>Entra numa sala encontrada na busca — sem digitar IP nenhum.</summary>
    public NetClient Join(NetRoomInfo room, string playerName = "Jogador")
    {
        var client = Join(NetClient.Create());
        client.Connect(room.Address, playerName);
        return client;
    }

    /// <summary>Assume um cliente já construído, ainda sem conectar — usado em teste.</summary>
    public NetClient Join(NetClient client)
    {
        Leave();
        StopBrowsing();

        _client = client;
        _client.PeerJoined += OnClientPeerJoined;
        _client.PeerLeft += OnClientPeerLeft;
        _client.Connected += OnClientConnected;
        _client.Disconnected += OnClientDisconnected;
        Rpc.Attach(client);
        Role = NetRole.Client;
        return client;
    }

    /// <summary>Encerra a sessão atual (avisando o outro lado) e volta pro modo offline.</summary>
    public void Leave()
    {
        Rpc.Detach();

        if (_host is not null)
        {
            _host.PeerJoined -= OnHostPeerJoined;
            _host.PeerLeft -= OnHostPeerLeft;
            _host.Dispose();
            _host = null;
        }

        if (_client is not null)
        {
            _client.PeerJoined -= OnClientPeerJoined;
            _client.PeerLeft -= OnClientPeerLeft;
            _client.Connected -= OnClientConnected;
            _client.Disconnected -= OnClientDisconnected;
            _client.Dispose();
            _client = null;
        }

        Role = NetRole.Offline;
    }

    /// <summary>Começa a procurar salas na rede local. Chame ao abrir a tela de partidas.</summary>
    /// <param name="hostPort">Porta onde os hosts escutam.</param>
    public NetBrowser StartBrowsing(int hostPort = NetProtocol.DefaultPort)
        => StartBrowsing(NetBrowser.Create(GameId, hostPort));

    /// <summary>Assume um navegador já construído — usado com <see cref="LoopbackNetwork"/> em teste.</summary>
    public NetBrowser StartBrowsing(NetBrowser browser)
    {
        StopBrowsing();

        Browser = browser;
        browser.Refresh();
        return browser;
    }

    /// <summary>Para de procurar e fecha o socket da busca. Chamado sozinho ao hospedar ou
    /// entrar numa sala.</summary>
    public void StopBrowsing()
    {
        Browser?.Dispose();
        Browser = null;
    }

    /// <summary>Liga a sessão a um mundo, habilitando a sincronização de entidades.
    /// Chamado uma vez por <c>Game</c>; chamar de novo devolve o mesmo sistema.</summary>
    public NetSyncSystem AttachWorld(World world) => Sync ??= new NetSyncSystem(this, world);

    /// <summary>Bombeia a rede. Chamado por <c>Game.HandleUpdate</c> antes da lógica do frame;
    /// no-op quando offline.</summary>
    public void Update(float deltaTime)
    {
        Browser?.Update(deltaTime);
        _host?.Update(deltaTime);
        _client?.Update(deltaTime);

        // Depois do host/cliente: a sincronização precisa dos pacotes deste frame já lidos,
        // senão trabalharia sempre com o estado do frame anterior.
        Sync?.Update(deltaTime);
    }

    private void OnHostPeerJoined(NetPeer peer) => PlayerJoined?.Invoke(peer);

    private void OnHostPeerLeft(NetPeer peer, NetDisconnectReason _) => PlayerLeft?.Invoke(peer);

    private void OnClientPeerJoined(NetPeer peer) => PlayerJoined?.Invoke(peer);

    private void OnClientPeerLeft(NetPeer peer) => PlayerLeft?.Invoke(peer);

    private void OnClientConnected(byte selfId) => JoinedRoom?.Invoke(selfId);

    private void OnClientDisconnected(NetDisconnectReason reason) => LeftRoom?.Invoke(reason);

    public void Dispose()
    {
        StopBrowsing();
        Leave();
    }
}
