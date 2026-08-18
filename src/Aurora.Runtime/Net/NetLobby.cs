namespace Aurora.Runtime.Net;

/// <summary>Em que ponto da tela de partidas o jogador está.</summary>
public enum NetLobbyState
{
    /// <summary>Nada acontecendo — menu principal.</summary>
    Idle = 0,

    /// <summary>Procurando salas na rede local.</summary>
    Browsing = 1,

    /// <summary>Hospedando e esperando os outros entrarem.</summary>
    Hosting = 2,

    /// <summary>Tentando entrar numa sala.</summary>
    Connecting = 3,

    /// <summary>Dentro da sala de outro jogador.</summary>
    InRoom = 4,

    /// <summary>Última tentativa falhou — ver <see cref="NetLobby.Message"/>.</summary>
    Failed = 5,
}

/// <summary>
/// A tela de partidas sem a parte visual: lista de salas encontradas, seleção, endereço
/// digitado, e os botões traduzidos em métodos.
///
/// <para>Não desenha nada de propósito. Cada jogo tem a própria arte, e uma tela pronta da
/// engine seria substituída no primeiro dia — o que ninguém quer reescrever é justamente o que
/// está aqui: o vaivém de estado, o índice que precisa continuar válido enquanto salas entram
/// e saem da lista, e a tradução dos motivos de falha em frase de tela.</para>
///
/// <para>Monte a tela com <c>UiButton</c> (polling de <c>Clicked</c>) e <c>UiTextInput</c>
/// (ligado a <see cref="Address"/>), e chame <see cref="Update"/> uma vez por frame.</para>
/// </summary>
public sealed class NetLobby
{
    private static readonly NetRoomInfo[] Empty = [];

    private readonly NetSession _session;

    public NetLobby(NetSession session)
    {
        _session = session;
        _session.JoinedRoom += OnJoinedRoom;
        _session.LeftRoom += OnLeftRoom;
    }

    public NetLobbyState State { get; private set; } = NetLobbyState.Idle;

    /// <summary>Nome deste jogador, mostrado aos outros.</summary>
    public string PlayerName { get; set; } = "Jogador";

    /// <summary>Nome da sala ao hospedar. Vazio vira o nome do jogador.</summary>
    public string RoomName { get; set; } = "";

    /// <summary>IP digitado à mão. Ligue direto no <c>UiTextInput.Text</c> da sua tela.</summary>
    public string Address { get; set; } = "";

    public int Port { get; set; } = NetProtocol.DefaultPort;

    /// <summary>Vagas ao hospedar, este jogador incluído.</summary>
    public int MaxPlayers { get; set; } = NetProtocol.MaxPlayersLimit;

    /// <summary>Salas encontradas na rede.</summary>
    public IReadOnlyList<NetRoomInfo> Rooms => _session.Rooms.Count > 0 ? _session.Rooms : Empty;

    /// <summary>Linha destacada da lista. Fica sempre dentro dos limites, mesmo com salas
    /// entrando e saindo enquanto o jogador navega.</summary>
    public int SelectedIndex { get; private set; }

    public NetRoomInfo? Selected
        => SelectedIndex >= 0 && SelectedIndex < Rooms.Count ? Rooms[SelectedIndex] : null;

    /// <summary>Frase pronta pra mostrar na tela — motivo da última falha, ou vazio.</summary>
    public string Message { get; private set; } = "";

    /// <summary>Endereço desta máquina, pra quem hospeda mostrar na tela.</summary>
    public string LocalAddress => UdpNetTransport.GetLocalAddress();

    /// <summary>Move a seleção. Aceita passar do fim (dá a volta), que é o que teclado e
    /// controle esperam numa lista.</summary>
    public void MoveSelection(int delta)
    {
        if (Rooms.Count == 0)
        {
            SelectedIndex = 0;
            return;
        }

        SelectedIndex = ((SelectedIndex + delta) % Rooms.Count + Rooms.Count) % Rooms.Count;
    }

    public void Select(int index) => SelectedIndex = index;

    /// <summary>Começa a procurar salas.</summary>
    public void Browse()
    {
        Message = "";
        SelectedIndex = 0;
        _session.StartBrowsing(Port);
        State = NetLobbyState.Browsing;
    }

    /// <summary>Pergunta ao endereço digitado sem entrar. A sala aparece na lista com nome e
    /// lotação, o que resolve o caso de rede que bloqueia broadcast e ainda mostra ao jogador
    /// que o IP está certo antes de tentar conectar.</summary>
    public void ProbeTyped()
    {
        if (string.IsNullOrWhiteSpace(Address)) return;
        if (_session.Browser is null) Browse();

        _session.Browser?.Probe(Address.Trim(), Port);
    }

    /// <summary>Passa a hospedar.</summary>
    public void Host()
    {
        Message = "";
        _session.RoomName = string.IsNullOrWhiteSpace(RoomName) ? PlayerName : RoomName;
        _session.StartHost(PlayerName, Port, MaxPlayers);
        State = NetLobbyState.Hosting;
    }

    /// <summary>Entra na sala destacada na lista.</summary>
    public bool JoinSelected()
    {
        if (Selected is not { } room) return false;

        if (room.IsFull)
        {
            Message = "Sala cheia.";
            State = NetLobbyState.Failed;
            return false;
        }

        Message = "";
        _session.Join(room, PlayerName);
        State = NetLobbyState.Connecting;
        return true;
    }

    /// <summary>Entra pelo endereço digitado.</summary>
    public bool JoinTyped()
    {
        string address = Address.Trim();
        if (address.Length == 0)
        {
            Message = "Digite o IP do host.";
            State = NetLobbyState.Failed;
            return false;
        }

        Message = "";
        _session.Join(address, Port, PlayerName);
        State = NetLobbyState.Connecting;
        return true;
    }

    /// <summary>Sai da sala, para de hospedar e para de procurar — o botão "voltar".</summary>
    public void Cancel()
    {
        _session.Leave();
        _session.StopBrowsing();
        Message = "";
        State = NetLobbyState.Idle;
    }

    /// <summary>Chame uma vez por frame enquanto a tela estiver aberta.</summary>
    public void Update(float deltaTime)
    {
        // A lista muda sozinha enquanto o jogador olha pra ela (sala nova apareceu, host
        // fechou o jogo). Sem isto, o destaque apontaria pra fora da lista e "entrar" não
        // faria nada, ou pior, entraria na sala errada.
        if (SelectedIndex >= Rooms.Count)
            SelectedIndex = Math.Max(0, Rooms.Count - 1);
    }

    private void OnJoinedRoom(byte selfId)
    {
        Message = "";
        State = NetLobbyState.InRoom;
    }

    private void OnLeftRoom(NetDisconnectReason reason)
    {
        // Sair por vontade própria não é falha, e mostrar "desconectado" depois de clicar em
        // voltar só confundiria.
        if (reason == NetDisconnectReason.Requested)
        {
            State = NetLobbyState.Idle;
            return;
        }

        Message = Describe(reason);
        State = NetLobbyState.Failed;
    }

    private string Describe(NetDisconnectReason reason) => reason switch
    {
        NetDisconnectReason.ConnectFailed => "Não foi possível conectar. Confira o IP e se o host está com o jogo aberto.",
        NetDisconnectReason.TimedOut => "A conexão caiu.",
        NetDisconnectReason.HostShutdown => "O host encerrou a partida.",
        NetDisconnectReason.Rejected => DescribeReject(),
        _ => "Desconectado.",
    };

    private string DescribeReject() => _session.Client?.LastRejectReason switch
    {
        NetRejectReason.Full => "Sala cheia.",
        NetRejectReason.VersionMismatch => "Versão do jogo diferente da do host.",
        _ => "O host recusou a conexão.",
    };
}
