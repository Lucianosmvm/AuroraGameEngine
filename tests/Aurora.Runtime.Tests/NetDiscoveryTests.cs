using System.Net;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Descoberta de salas na rede local — o que tira a necessidade de digitar IP. Funciona por
/// pergunta e resposta na mesma porta do jogo.
/// </summary>
public class NetDiscoveryTests : IDisposable
{
    private const int HostPort = 7777;
    private const string GameId = "MeuJogo";
    private const float Step = 0.1f;

    private readonly LoopbackNetwork _net = new();
    private readonly List<IDisposable> _abrir = [];

    private NetHost CreateHost(string hostName = "Host", string roomName = "", int maxPlayers = 8,
        string gameId = GameId)
    {
        var host = new NetHost(_net.CreateTransport(HostPort), hostName, maxPlayers)
        {
            GameId = gameId,
            RoomName = roomName,
        };

        _abrir.Add(host);
        return host;
    }

    private NetBrowser CreateBrowser(string gameId = GameId)
    {
        var browser = new NetBrowser(_net.CreateTransport(), gameId, HostPort)
        {
            // Fixo em vez do padrão (que varre as placas de rede da máquina real): teste não
            // pode depender de quantas interfaces o computador que roda a suíte tem.
            BroadcastTargets = [new IPEndPoint(IPAddress.Broadcast, HostPort)],

            // Pergunta a cada passo do teste, pra sala não envelhecer no meio de uma
            // sequência longa. Em jogo o padrão de 1 s é suficiente.
            ProbeInterval = Step,
        };

        _abrir.Add(browser);
        return browser;
    }

    private static void Pump(NetHost host, NetBrowser browser, int steps = 3, float dt = Step)
    {
        for (int i = 0; i < steps; i++)
        {
            browser.Update(dt);
            host.Update(dt);
            browser.Update(dt);
        }
    }

    public void Dispose()
    {
        foreach (var item in _abrir)
            item.Dispose();
    }

    [Fact]
    public void HostApareceNaBusca()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var browser = CreateBrowser();

        browser.Refresh();
        Pump(host, browser);

        var sala = Assert.Single(browser.Rooms);
        Assert.Equal("Sala da Ana", sala.RoomName);
        Assert.Equal("Ana", sala.HostName);
        Assert.Equal(1, sala.PlayerCount);
        Assert.Equal(8, sala.MaxPlayers);
        Assert.Equal(HostPort, sala.Address.Port);
    }

    [Fact]
    public void SemNomeDeSalaUsaONomeDeQuemHospeda()
    {
        var host = CreateHost("Bruno");
        var browser = CreateBrowser();

        browser.Refresh();
        Pump(host, browser);

        Assert.Equal("Bruno", Assert.Single(browser.Rooms).RoomName);
    }

    [Fact]
    public void LotacaoAcompanhaQuemEntra()
    {
        var host = CreateHost();
        var browser = CreateBrowser();

        browser.Refresh();
        Pump(host, browser);
        Assert.Equal(1, Assert.Single(browser.Rooms).PlayerCount);

        using var cliente = new NetClient(_net.CreateTransport());
        cliente.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), "Ana");

        for (int i = 0; i < 3; i++)
        {
            host.Update(Step);
            cliente.Update(Step);
        }

        browser.Refresh();
        Pump(host, browser);

        Assert.Equal(2, Assert.Single(browser.Rooms).PlayerCount);
    }

    [Fact]
    public void OutroJogoNaMesmaRedeNaoAparece()
    {
        var host = CreateHost(gameId: "OutroJogo");
        var browser = CreateBrowser();

        browser.Refresh();
        Pump(host, browser);

        Assert.Empty(browser.Rooms);
    }

    [Fact]
    public void HostPodeSeEsconderDaBusca()
    {
        var host = CreateHost();
        host.Discoverable = false;

        var browser = CreateBrowser();
        browser.Refresh();
        Pump(host, browser);

        // Escondido não some da rede: quem souber o IP ainda entra.
        Assert.Empty(browser.Rooms);

        using var cliente = new NetClient(_net.CreateTransport());
        cliente.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), "Ana");

        for (int i = 0; i < 3; i++)
        {
            host.Update(Step);
            cliente.Update(Step);
        }

        Assert.Equal(NetClientState.Connected, cliente.State);
    }

    [Fact]
    public void SalaCheiaApareceMarcadaEmVezDeSumir()
    {
        // maxPlayers 1: o host sozinho já lota.
        var host = CreateHost(maxPlayers: 1);
        var browser = CreateBrowser();

        browser.Refresh();
        Pump(host, browser);

        var sala = Assert.Single(browser.Rooms);
        Assert.True(sala.IsFull);

        // Sumir da lista faria o jogador achar que digitou algo errado; aparecer como "1/1"
        // explica sozinho por que não dá pra entrar.
        Assert.Equal(1, sala.PlayerCount);
        Assert.Equal(1, sala.MaxPlayers);
    }

    [Fact]
    public void SalaQueParouDeResponderSaiDaLista()
    {
        var host = CreateHost();
        var browser = CreateBrowser();
        browser.RoomTimeout = 0.5f;

        browser.Refresh();
        Pump(host, browser);
        Assert.Single(browser.Rooms);

        // Host fechou o jogo: ninguém mais responde às perguntas.
        host.Dispose();
        for (int i = 0; i < 10; i++)
            browser.Update(Step);

        Assert.Empty(browser.Rooms);
    }

    [Fact]
    public void PerguntaDiretaAchaSalaSemBroadcast()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var browser = CreateBrowser();

        // Rede que bloqueia broadcast (Wi-Fi de empresa, roteador com isolamento de cliente):
        // o jogador digita o IP e a sala aparece na lista igual às outras.
        browser.BroadcastTargets = [];
        browser.Probe("127.0.0.1", HostPort);
        Pump(host, browser);

        Assert.Equal("Sala da Ana", Assert.Single(browser.Rooms).RoomName);
    }

    [Fact]
    public void RespostaRepetidaNaoDuplicaASala()
    {
        var host = CreateHost();
        var browser = CreateBrowser();

        for (int i = 0; i < 5; i++)
        {
            browser.Refresh();
            Pump(host, browser);
        }

        Assert.Single(browser.Rooms);
    }

    [Fact]
    public void MudancaNaListaAvisaQuemEstaOlhando()
    {
        var host = CreateHost();
        var browser = CreateBrowser();
        browser.RoomTimeout = 0.5f;

        int avisos = 0;
        browser.RoomsChanged += () => avisos++;

        browser.Refresh();
        Pump(host, browser);
        Assert.Equal(1, avisos);

        // Perguntas repetidas sem nada mudar não avisam de novo — senão a tela redesenharia a
        // lista todo segundo sem motivo.
        browser.Refresh();
        Pump(host, browser);
        Assert.Equal(1, avisos);

        host.Dispose();
        for (int i = 0; i < 10; i++)
            browser.Update(Step);

        Assert.Equal(2, avisos);
        Assert.Empty(browser.Rooms);
    }

    [Fact]
    public void DuasSalasAparecemSeparadas()
    {
        var ana = CreateHost("Ana", "Sala da Ana");
        var bruno = new NetHost(_net.CreateTransport(7778), "Bruno", 8)
        {
            GameId = GameId,
            RoomName = "Sala do Bruno",
        };
        _abrir.Add(bruno);

        var browser = CreateBrowser();

        // O broadcast é por porta: cada host escuta na sua, então pergunta-se nas duas.
        browser.BroadcastTargets =
        [
            new IPEndPoint(IPAddress.Broadcast, HostPort),
            new IPEndPoint(IPAddress.Broadcast, 7778),
        ];

        browser.Refresh();
        for (int i = 0; i < 3; i++)
        {
            browser.Update(Step);
            ana.Update(Step);
            bruno.Update(Step);
            browser.Update(Step);
        }

        Assert.Equal(2, browser.Rooms.Count);
        Assert.Contains(browser.Rooms, r => r.RoomName == "Sala da Ana");
        Assert.Contains(browser.Rooms, r => r.RoomName == "Sala do Bruno");
    }

    [Fact]
    public void MesmoHostRespondendoPorVariosCaminhosAparaceUmaVezSo()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var browser = CreateBrowser();

        // Imita PC com Wi-Fi + cabo + VPN: o mesmo host recebe a pergunta por vários caminhos
        // e responde por cada um, com origem diferente. Uma sala, não três.
        browser.BroadcastTargets =
        [
            new IPEndPoint(IPAddress.Broadcast, HostPort),
            new IPEndPoint(IPAddress.Parse("192.168.0.255"), HostPort),
            new IPEndPoint(IPAddress.Parse("10.0.0.255"), HostPort),
        ];

        browser.Refresh();
        Pump(host, browser);

        Assert.Single(browser.Rooms);
        Assert.Equal(host.RoomId, browser.Rooms[0].RoomId);
    }

    [Fact]
    public void ClearEsvaziaALista()
    {
        var host = CreateHost();
        var browser = CreateBrowser();

        browser.Refresh();
        Pump(host, browser);
        Assert.Single(browser.Rooms);

        browser.Clear();
        Assert.Empty(browser.Rooms);
    }

    [Fact]
    public void EntrarNumaSalaEncontradaFunciona()
    {
        var host = CreateHost("Ana", "Sala da Ana");
        var browser = CreateBrowser();

        browser.Refresh();
        Pump(host, browser);

        var sala = Assert.Single(browser.Rooms);

        using var cliente = new NetClient(_net.CreateTransport());
        cliente.Connect(sala.Address, "Bruno");

        for (int i = 0; i < 3; i++)
        {
            host.Update(Step);
            cliente.Update(Step);
        }

        // O endereço veio de onde a resposta chegou, não do que o pacote dizia — e serve
        // direto pra conectar.
        Assert.Equal(NetClientState.Connected, cliente.State);
        Assert.Equal(2, host.PlayerCount);
    }
}
