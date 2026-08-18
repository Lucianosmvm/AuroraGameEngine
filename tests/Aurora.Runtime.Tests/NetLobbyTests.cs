using System.Net;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// A tela de partidas sem a parte visual: seleção que sobrevive à lista mudando embaixo dela
/// e motivos de falha traduzidos em frase de tela.
/// </summary>
public class NetLobbyTests : IDisposable
{
    private const int HostPort = 7777;
    private const string GameId = "MeuJogo";
    private const float Step = 0.1f;

    private readonly LoopbackNetwork _net = new();
    private readonly List<IDisposable> _abrir = [];

    private NetHost CreateHost(string hostName, string roomName, int port = HostPort, int maxPlayers = 8)
    {
        var host = new NetHost(_net.CreateTransport(port), hostName, maxPlayers)
        {
            GameId = GameId,
            RoomName = roomName,
        };

        _abrir.Add(host);
        return host;
    }

    private (NetSession Session, NetLobby Lobby) CreateLobby()
    {
        var session = new NetSession { GameId = GameId };
        _abrir.Add(session);

        return (session, new NetLobby(session));
    }

    /// <summary>Liga a busca do lobby num navegador de loopback, no lugar do socket real.</summary>
    private NetBrowser AttachBrowser(NetSession session, params int[] ports)
    {
        var browser = new NetBrowser(_net.CreateTransport(), GameId, HostPort)
        {
            BroadcastTargets = ports.Select(p => new IPEndPoint(IPAddress.Broadcast, p)).ToArray(),
            ProbeInterval = Step,
        };

        return session.StartBrowsing(browser);
    }

    private void Pump(NetSession session, NetLobby lobby, IEnumerable<NetHost> hosts, int steps = 3)
    {
        for (int i = 0; i < steps; i++)
        {
            session.Update(Step);
            foreach (var host in hosts)
                host.Update(Step);

            session.Update(Step);
            lobby.Update(Step);
        }
    }

    public void Dispose()
    {
        foreach (var item in _abrir)
            item.Dispose();
    }

    [Fact]
    public void ComecaParadoESemMensagem()
    {
        var (_, lobby) = CreateLobby();

        Assert.Equal(NetLobbyState.Idle, lobby.State);
        Assert.Empty(lobby.Message);
        Assert.Empty(lobby.Rooms);
        Assert.Null(lobby.Selected);
    }

    [Fact]
    public void SalasEncontradasAparecemNoLobby()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var (session, lobby) = CreateLobby();
        AttachBrowser(session, HostPort);
        Pump(session, lobby, [host]);

        Assert.Single(lobby.Rooms);
        Assert.Equal("Sala da Ana", lobby.Selected?.RoomName);
    }

    [Fact]
    public void SelecaoDaAVoltaNasPontas()
    {
        var ana = CreateHost("Ana", "Sala da Ana");
        var bruno = CreateHost("Bruno", "Sala do Bruno", port: 7778);

        var (session, lobby) = CreateLobby();
        AttachBrowser(session, HostPort, 7778);
        Pump(session, lobby, [ana, bruno]);

        Assert.Equal(2, lobby.Rooms.Count);
        Assert.Equal(0, lobby.SelectedIndex);

        lobby.MoveSelection(1);
        Assert.Equal(1, lobby.SelectedIndex);

        // Passar do fim volta pro começo — é o que teclado e controle esperam numa lista.
        lobby.MoveSelection(1);
        Assert.Equal(0, lobby.SelectedIndex);

        lobby.MoveSelection(-1);
        Assert.Equal(1, lobby.SelectedIndex);
    }

    [Fact]
    public void SelecaoContinuaValidaQuandoASalaSome()
    {
        var ana = CreateHost("Ana", "Sala da Ana");
        var bruno = CreateHost("Bruno", "Sala do Bruno", port: 7778);

        var (session, lobby) = CreateLobby();
        var browser = AttachBrowser(session, HostPort, 7778);
        browser.RoomTimeout = 0.5f;
        Pump(session, lobby, [ana, bruno]);

        lobby.MoveSelection(1);
        Assert.Equal(1, lobby.SelectedIndex);

        // Um dos hosts fecha o jogo enquanto o jogador olha a lista.
        ana.Dispose();
        bruno.Dispose();
        for (int i = 0; i < 10; i++)
        {
            session.Update(Step);
            lobby.Update(Step);
        }

        // Sem o ajuste, o destaque apontaria pra fora da lista e "entrar" não faria nada.
        Assert.Empty(lobby.Rooms);
        Assert.Equal(0, lobby.SelectedIndex);
        Assert.Null(lobby.Selected);
        Assert.False(lobby.JoinSelected());
    }

    [Fact]
    public void EntrarSemSelecaoNaoFazNada()
    {
        var (_, lobby) = CreateLobby();

        Assert.False(lobby.JoinSelected());
        Assert.Equal(NetLobbyState.Idle, lobby.State);
    }

    [Fact]
    public void SalaCheiaNemTentaConectar()
    {
        var host = CreateHost("Ana", "Sala da Ana", maxPlayers: 1);
        var (session, lobby) = CreateLobby();
        AttachBrowser(session, HostPort);
        Pump(session, lobby, [host]);

        Assert.True(lobby.Selected?.IsFull);
        Assert.False(lobby.JoinSelected());

        // Melhor avisar na hora do que gastar o handshake inteiro pra ouvir a mesma coisa.
        Assert.Equal(NetLobbyState.Failed, lobby.State);
        Assert.Equal("Sala cheia.", lobby.Message);
    }

    [Fact]
    public void IpVazioAvisaEmVezDeTentar()
    {
        var (_, lobby) = CreateLobby();
        lobby.Address = "   ";

        Assert.False(lobby.JoinTyped());
        Assert.Equal(NetLobbyState.Failed, lobby.State);
        Assert.Equal("Digite o IP do host.", lobby.Message);
    }

    [Fact]
    public void EntrarNumaSalaLevaOLobbyPraDentro()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var (session, lobby) = CreateLobby();

        var client = session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), "Bruno");

        Pump(session, lobby, [host]);

        Assert.Equal(NetLobbyState.InRoom, lobby.State);
        Assert.Empty(lobby.Message);
    }

    [Fact]
    public void FalhaDeConexaoVemComFraseDeTela()
    {
        var (session, lobby) = CreateLobby();

        // Ninguém escutando nessa porta.
        var client = session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, 9999), "Ana");

        for (int i = 0; i < 8; i++)
        {
            session.Update(1f);
            lobby.Update(1f);
        }

        Assert.Equal(NetLobbyState.Failed, lobby.State);
        Assert.Contains("Confira o IP", lobby.Message);
    }

    [Fact]
    public void RecusaPorSalaCheiaViraFraseDeTela()
    {
        var host = CreateHost("Ana", "Sala da Ana", maxPlayers: 1);
        var (session, lobby) = CreateLobby();

        var client = session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), "Bruno");

        Pump(session, lobby, [host]);

        Assert.Equal(NetLobbyState.Failed, lobby.State);
        Assert.Equal("Sala cheia.", lobby.Message);
    }

    [Fact]
    public void HostEncerrandoAPartidaVemComFraseDeTela()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var (session, lobby) = CreateLobby();

        var client = session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), "Bruno");
        Pump(session, lobby, [host]);

        Assert.Equal(NetLobbyState.InRoom, lobby.State);

        host.Shutdown();
        session.Update(Step);

        Assert.Equal(NetLobbyState.Failed, lobby.State);
        Assert.Equal("O host encerrou a partida.", lobby.Message);
    }

    [Fact]
    public void SairDePropositoNaoVirouFalha()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var (session, lobby) = CreateLobby();

        var client = session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), "Bruno");
        Pump(session, lobby, [host]);

        client.Disconnect();

        // Mostrar "desconectado" depois de clicar em voltar só confundiria.
        Assert.Equal(NetLobbyState.Idle, lobby.State);
        Assert.Empty(lobby.Message);
    }

    [Fact]
    public void CancelFechaTudoEVoltaProInicio()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var (session, lobby) = CreateLobby();
        AttachBrowser(session, HostPort);
        Pump(session, lobby, [host]);

        Assert.NotEmpty(lobby.Rooms);

        lobby.Cancel();

        Assert.Equal(NetLobbyState.Idle, lobby.State);
        Assert.Null(session.Browser);
        Assert.True(session.IsOffline);
        Assert.Empty(lobby.Rooms);
    }
}
