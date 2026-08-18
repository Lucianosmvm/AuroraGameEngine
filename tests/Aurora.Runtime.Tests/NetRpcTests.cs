using System.Net;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Eventos nomeados entre as máquinas: som, dano, porta abrindo. Diferente do snapshot, não
/// podem se perder — vão pelo canal confiável.
/// </summary>
public class NetRpcTests : IDisposable
{
    private const int HostPort = 7777;
    private const float Step = 0.05f;

    private readonly LoopbackNetwork _net = new();
    private readonly List<Machine> _machines = [];

    private sealed class Machine
    {
        public required NetSession Session { get; init; }

        /// <summary>Tudo que chegou aqui, na ordem, como "nome:remetente:arg0".</summary>
        public List<string> Received { get; } = [];
    }

    private Machine CreateMachine()
    {
        var machine = new Machine { Session = new NetSession() };
        _machines.Add(machine);
        return machine;
    }

    private Machine CreateHost()
    {
        var machine = CreateMachine();
        machine.Session.StartHost(new NetHost(_net.CreateTransport(HostPort), "Host"));
        return machine;
    }

    private Machine CreateClient(string name)
    {
        var machine = CreateMachine();
        var client = machine.Session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), name);
        return machine;
    }

    /// <summary>Registra o mesmo handler de log em todas as máquinas.</summary>
    private void ListenEverywhere(string name)
    {
        foreach (var machine in _machines)
        {
            var target = machine;
            target.Session.Rpc.On(name, args => target.Received.Add($"{args.Name}:{args.SenderId}:{args.GetString(0)}"));
        }
    }

    private void Pump(int steps = 4)
    {
        for (int i = 0; i < steps; i++)
        {
            foreach (var machine in _machines)
                machine.Session.Update(Step);
        }
    }

    public void Dispose()
    {
        foreach (var machine in _machines)
            machine.Session.Dispose();
    }

    /// <summary>Mesmo FNV-1a que o <see cref="NetRpcSystem"/> usa. Duplicado de propósito: se
    /// alguém mudar o algoritmo lá, este teste quebra e mostra que o formato do fio mudou.</summary>
    private static uint Hash(string name)
    {
        uint hash = 2166136261;
        foreach (char c in name)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }

    [Fact]
    public void HostMandaProTodosETodosRecebem()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        ListenEverywhere("Explosao");
        host.Session.Rpc.Send("Explosao", "torre");
        Pump();

        Assert.Equal(["Explosao:0:torre"], host.Received);
        Assert.Equal(["Explosao:0:torre"], ana.Received);
        Assert.Equal(["Explosao:0:torre"], bruno.Received);
    }

    [Fact]
    public void ClienteFalaComOHost()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        ListenEverywhere("Comprar");
        ana.Session.Rpc.Send(NetRpcTarget.Host, "Comprar", "espada");
        Pump();

        // O remetente que o host vê é o id real do peer, não o que o pacote afirma.
        Assert.Equal(["Comprar:1:espada"], host.Received);
        Assert.Empty(ana.Received);
        Assert.Empty(bruno.Received);
    }

    [Fact]
    public void ClienteMandaProTodosInclusiveEleMesmo()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        ListenEverywhere("Emote");
        ana.Session.Rpc.Send("Emote", "aceno");
        Pump();

        Assert.Equal(["Emote:1:aceno"], host.Received);
        Assert.Equal(["Emote:1:aceno"], ana.Received);
        Assert.Equal(["Emote:1:aceno"], bruno.Received);
    }

    [Fact]
    public void OthersNaoVoltaProRemetente()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        ListenEverywhere("Grito");
        ana.Session.Rpc.Send(NetRpcTarget.Others, "Grito", "ei");
        Pump();

        Assert.Empty(ana.Received);
        Assert.Equal(["Grito:1:ei"], host.Received);
        Assert.Equal(["Grito:1:ei"], bruno.Received);
    }

    [Fact]
    public void MensagemPrivadaSoChegaNoAlvo()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        ListenEverywhere("Sussurro");
        host.Session.Rpc.SendToPlayer(bruno.Session.SelfId, "Sussurro", "só pra você");
        Pump();

        Assert.Equal(["Sussurro:0:só pra você"], bruno.Received);
        Assert.Empty(ana.Received);
        Assert.Empty(host.Received);
    }

    [Fact]
    public void ClienteMandaPrivadoProOutroPassandoPeloHost()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        ListenEverywhere("Troca");
        ana.Session.Rpc.SendToPlayer(bruno.Session.SelfId, "Troca", "poção");
        Pump();

        Assert.Equal(["Troca:1:poção"], bruno.Received);
        Assert.Empty(ana.Received);
        Assert.Empty(host.Received);
    }

    [Fact]
    public void RemetenteNaoPodeSerForjado()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump();

        ListenEverywhere("Admin");

        // Cliente montando o pacote na mão e se declarando host (id 0).
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Rpc);
        writer.WriteUInt32(Hash("Admin"));
        writer.WriteByte(NetProtocol.HostId);
        writer.WriteByte((byte)NetRpcTarget.Host);
        writer.WriteByte(0);
        writer.WriteByte(1);
        writer.WriteByte((byte)NetRpcArgKind.String);
        writer.WriteString("banir todo mundo");

        ana.Session.Client!.SendReliable(writer.Written);
        Pump();

        // O host reescreve o remetente com o id de quem realmente mandou.
        Assert.Equal(["Admin:1:banir todo mundo"], host.Received);
    }

    [Fact]
    public void ArgumentosSobrevivemAViagem()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump();

        NetRpcArgs? recebido = null;
        ana.Session.Rpc.On("Dano", args => recebido = args);

        host.Session.Rpc.Send("Dano", 42, 12.5f, true, "espinho");
        Pump();

        Assert.NotNull(recebido);
        Assert.Equal(4, recebido!.Count);
        Assert.Equal(42, recebido.GetInt(0));
        Assert.Equal(12.5f, recebido.GetFloat(1), 0.001f);
        Assert.True(recebido.GetBool(2));
        Assert.Equal("espinho", recebido.GetString(3));

        // Índice fora da lista devolve o fallback em vez de estourar.
        Assert.Equal(-1, recebido.GetInt(99, -1));
    }

    [Fact]
    public void NumeroMandadoComoIntSaiTambemComoFloat()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump();

        NetRpcArgs? recebido = null;
        ana.Session.Rpc.On("Vida", args => recebido = args);

        host.Session.Rpc.Send("Vida", 30);
        Pump();

        Assert.NotNull(recebido);
        Assert.Equal(30f, recebido!.GetFloat(0), 0.001f);
        Assert.Equal("30", recebido.GetString(0));
    }

    [Fact]
    public void RpcQueEstaMaquinaNaoConheceEIgnorado()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump();

        // Só o host registra. O cliente recebe e não faz nada — build diferente, ou evento que
        // só interessa a um dos lados. Não é erro.
        host.Session.Rpc.On("SoNoHost", args => host.Received.Add(args.Name));

        host.Session.Rpc.Send("SoNoHost");
        Pump();

        Assert.Equal(["SoNoHost"], host.Received);
        Assert.Empty(ana.Received);
    }

    [Fact]
    public void OfflineEntregaLocalmente()
    {
        using var session = new NetSession();

        var recebidos = new List<string>();
        session.Rpc.On("Salvar", args => recebidos.Add(args.GetString(0)));

        session.Rpc.Send("Salvar", "slot1");
        session.Rpc.Send(NetRpcTarget.Host, "Salvar", "slot2");

        // Mesmo código roda em jogo de um jogador só, sem if de "está em rede?".
        Assert.Equal(["slot1", "slot2"], recebidos);
    }

    [Fact]
    public void ChegaMesmoComPerdaPesadaDePacote()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump();

        ListenEverywhere("Morreu");

        _net.PacketLoss = 0.7f;
        host.Session.Rpc.Send(NetRpcTarget.Others, "Morreu", "chefe");
        Pump(20);

        _net.PacketLoss = 0f;
        Pump(12);

        Assert.Equal(["Morreu:0:chefe"], ana.Received);
    }

    [Fact]
    public void OrdemEPreservadaNaRajadaComPerda()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump();

        var recebidos = new List<int>();
        ana.Session.Rpc.On("Passo", args => recebidos.Add(args.GetInt(0)));

        _net.PacketLoss = 0.5f;
        for (int i = 0; i < 20; i++)
        {
            host.Session.Rpc.Send(NetRpcTarget.Others, "Passo", i);
            Pump(1);
        }

        _net.PacketLoss = 0f;
        Pump(30);

        Assert.Equal(Enumerable.Range(0, 20), recebidos);
    }

    [Fact]
    public void ClientePodeSerImpedidoDeFalarComASalaInteira()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        ListenEverywhere("Spam");
        host.Session.Rpc.AllowClientBroadcast = false;

        ana.Session.Rpc.Send("Spam", "ninguém pediu");
        Pump();

        // A entrega local do próprio remetente acontece antes de sair o pacote e não dá pro
        // host impedir — o que ele barra é a retransmissão pros outros.
        Assert.Equal(["Spam:1:ninguém pediu"], ana.Received);
        Assert.Empty(host.Received);
        Assert.Empty(bruno.Received);

        // Falar com o host continua liberado: é assim que o cliente pede as coisas.
        ana.Session.Rpc.Send(NetRpcTarget.Host, "Spam", "posso?");
        Pump();

        Assert.Equal(["Spam:1:posso?"], host.Received);
    }

    [Fact]
    public void ArgumentoDeTipoNaoSuportadoEErroNaHora()
    {
        using var session = new NetSession();

        Assert.Throws<ArgumentException>(() => session.Rpc.Send("X", new object()));
    }

    [Fact]
    public void ArgumentosDemaisSaoErro()
    {
        using var session = new NetSession();

        object?[] demais = Enumerable.Range(0, NetProtocol.MaxRpcArgs + 1).Cast<object?>().ToArray();

        Assert.Throws<ArgumentException>(() => session.Rpc.Send("X", demais));
    }

    [Fact]
    public void RegistrarDeNovoSubstituiOHandler()
    {
        using var session = new NetSession();

        var recebidos = new List<string>();
        session.Rpc.On("Evento", _ => recebidos.Add("primeiro"));
        session.Rpc.On("Evento", _ => recebidos.Add("segundo"));

        session.Rpc.Send("Evento");

        Assert.Equal(["segundo"], recebidos);
        Assert.True(session.Rpc.Off("Evento"));
        Assert.False(session.Rpc.Off("Evento"));
    }
}
