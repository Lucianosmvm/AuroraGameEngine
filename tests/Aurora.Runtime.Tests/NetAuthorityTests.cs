using System.Net;
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Modo <see cref="NetAuthority.Host"/>: o cliente manda o que está apertando, o host simula e
/// devolve o resultado, e o cliente prevê localmente pra não sentir a viagem de ida e volta.
/// É o modo que impede um cliente modificado de inventar a própria posição.
/// </summary>
public class NetAuthorityTests : IDisposable
{
    private const int HostPort = 7777;
    private const byte PlayerPrefab = 1;
    private const float Speed = 200f;
    private const float Step = 1f / 60f;
    private const float Tolerance = 0.05f;

    /// <summary>Movimento padrão dos testes: anda na direção do eixo. Roda igual no host e no
    /// cliente — é o contrato do modo autoritativo.</summary>
    private static void Move(Entity entity, in NetInput input)
    {
        if (entity.Get<Transform>() is not { } transform) return;

        transform.Position += new Vector2(input.AxisX, input.AxisY) * Speed * input.DeltaTime;
    }

    /// <summary>Movimento do host num teste de divergência: igual ao de cima, mas trava em
    /// X=50 (uma parede que o cliente não previu).</summary>
    private static void MoveWithWall(Entity entity, in NetInput input)
    {
        Move(entity, in input);

        if (entity.Get<Transform>() is not { } transform) return;

        transform.Position = transform.Position with { X = MathF.Min(transform.Position.X, 50f) };
    }

    private readonly LoopbackNetwork _net = new();
    private readonly List<Machine> _machines = [];

    private sealed class Machine
    {
        public required World World { get; init; }
        public required NetSession Session { get; init; }
        public required NetSyncSystem Sync { get; init; }

        /// <summary>O que este jogador está apertando agora — os testes escrevem aqui.</summary>
        public NetInputState Input;

        public Entity? FindByNetId(ushort netId)
        {
            foreach (var (entity, _, identity) in World.Query<Transform, NetworkIdentity>())
            {
                if (identity.NetId == netId) return entity;
            }

            return null;
        }

        public float XOf(ushort netId)
            => FindByNetId(netId)?.Get<Transform>()?.Position.X
               ?? throw new InvalidOperationException($"NetId {netId} não existe nesta máquina.");

        public int SyncedEntityCount => World.Query<Transform, NetworkIdentity>().Count();
    }

    private Machine CreateMachine(NetMoveFunc? move = null)
    {
        var world = new World();
        var session = new NetSession();
        var sync = session.AttachWorld(world);

        sync.Authority = NetAuthority.Host;
        sync.InterpolationDelay = 0f;

        var machine = new Machine { World = world, Session = session, Sync = sync };
        sync.SampleInput = () => machine.Input;

        sync.Prefabs.Register(PlayerPrefab, static (world, identity) =>
        {
            var entity = world.CreateEntity($"Player{identity.OwnerId}");
            entity.Add(new Transform());
            return entity;
        }, move ?? Move);

        _machines.Add(machine);
        return machine;
    }

    private Machine CreateHost(NetMoveFunc? move = null)
    {
        var machine = CreateMachine(move);
        machine.Session.StartHost(new NetHost(_net.CreateTransport(HostPort), "Host"));
        return machine;
    }

    private Machine CreateClient(string name, NetMoveFunc? move = null)
    {
        var machine = CreateMachine(move);
        var client = machine.Session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), name);
        return machine;
    }

    private void Pump(int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            foreach (var machine in _machines)
                machine.Session.Update(Step);
        }
    }

    /// <summary>Host cria o boneco do jogador e todo mundo espera até ele aparecer.</summary>
    private ushort SpawnPlayer(Machine host, byte ownerId)
    {
        var entity = host.Sync.Spawn(PlayerPrefab, ownerId);
        ushort netId = entity.Get<NetworkIdentity>()!.NetId;

        Pump(6);
        return netId;
    }

    public void Dispose()
    {
        foreach (var machine in _machines)
            machine.Session.Dispose();
    }

    [Fact]
    public void ModoPadraoContinuaSendoOwner()
    {
        var world = new World();
        using var session = new NetSession();

        Assert.Equal(NetAuthority.Owner, session.AttachWorld(world).Authority);
    }

    [Fact]
    public void InputDoClienteMoveOBonecoNoHost()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);

        float inicio = host.XOf(netId);
        client.Input = new NetInputState(1f, 0f, 0u);
        Pump(30);
        client.Input = default;
        Pump(4);

        // Quem calculou a posição foi o host, a partir do que o cliente pediu.
        Assert.True(host.XOf(netId) > inicio + 50f, $"host andou só {host.XOf(netId) - inicio:F1} px");
    }

    [Fact]
    public void ClientePreveSemEsperarRespostaDoHost()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);

        float antes = client.XOf(netId);
        client.Input = new NetInputState(1f, 0f, 0u);

        // Um único frame do cliente, sem deixar o host rodar: se dependesse da resposta, o
        // boneco não teria saído do lugar.
        client.Session.Update(Step);

        Assert.Equal(antes + Speed * Step, client.XOf(netId), Tolerance);
    }

    [Fact]
    public void PrevisaoCertaNaoCausaCorrecaoVisivel()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);

        client.Input = new NetInputState(1f, 0f, 0u);

        float anterior = client.XOf(netId);
        float maiorSalto = 0f;

        for (int i = 0; i < 60; i++)
        {
            Pump(1);

            float atual = client.XOf(netId);
            maiorSalto = MathF.Max(maiorSalto, MathF.Abs(atual - anterior));
            anterior = atual;
        }

        // Movimento contínuo: cada frame anda o mesmo tanto. Se a reconciliação estivesse
        // empurrando o boneco pra trás, apareceria aqui como um salto bem maior.
        Assert.Equal(Speed * Step, maiorSalto, 0.2f);
    }

    [Fact]
    public void InputRepetidoNaoEAplicadoDuasVezes()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);

        // Cada pacote leva 3 frames de input (o atual e os 2 anteriores). Sem a checagem de
        // "já processei essa sequência", o boneco andaria o triplo.
        Assert.Equal(3, client.Sync.InputRedundancy);

        float inicio = host.XOf(netId);
        const int frames = 30;

        client.Input = new NetInputState(1f, 0f, 0u);
        Pump(frames);
        client.Input = default;
        Pump(6);

        float esperado = frames * Speed * Step;
        Assert.Equal(esperado, host.XOf(netId) - inicio, 1f);
    }

    [Fact]
    public void PerdaDePacoteNaoPerdeFrameDeInput()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);
        client.Sync.InputRedundancy = 8;

        float inicio = host.XOf(netId);
        const int frames = 60;

        // Metade dos pacotes some. Com 8 frames repetidos por pacote, um frame só se perde se
        // 8 pacotes seguidos caírem — 0,4% de chance.
        _net.PacketLoss = 0.5f;
        client.Input = new NetInputState(1f, 0f, 0u);
        Pump(frames);
        client.Input = default;

        _net.PacketLoss = 0f;
        Pump(6);

        float esperado = frames * Speed * Step;
        float andou = host.XOf(netId) - inicio;

        Assert.True(andou >= esperado * 0.97f, $"host andou {andou:F1} de {esperado:F1} esperados");
        Assert.True(andou <= esperado + 1f, $"host andou demais: {andou:F1} contra {esperado:F1}");
    }

    [Fact]
    public void ClienteEPuxadoDeVoltaQuandoOHostDiscorda()
    {
        // Host tem uma parede em X=50 que o cliente não conhece.
        var host = CreateHost(MoveWithWall);
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);

        client.Input = new NetInputState(1f, 0f, 0u);
        Pump(60);

        // Sem reconciliação, o cliente estaria em ~200. Com ela, fica preso na parede do host.
        Assert.Equal(50f, host.XOf(netId), 0.5f);
        Assert.True(client.XOf(netId) < 60f, $"cliente atravessou a parede: X={client.XOf(netId):F1}");
    }

    [Fact]
    public void HostIgnoraPosicaoCruaNoModoAutoritativo()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);
        float antes = host.XOf(netId);

        // Cliente modificado tentando o caminho da fase 2: mandar posição pronta em vez de
        // input. No modo autoritativo o host não aceita esse tipo de mensagem.
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.OwnedState);
        writer.WriteUInt16(1);
        writer.WriteByte(1);
        writer.WriteUInt16(netId);
        writer.WriteSingle(9999f);
        writer.WriteSingle(9999f);
        writer.WriteSingle(0f);

        client.Session.Client!.Send(writer.Written);
        Pump(4);

        Assert.Equal(antes, host.XOf(netId), Tolerance);
    }

    [Fact]
    public void FrameDeInputGiganteELimitado()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);
        float antes = host.XOf(netId);

        // Cliente modificado pedindo um frame de 10 segundos pra atravessar o mapa de uma vez.
        SendRawInput(client, sequence: 50_000, deltaTime: 10f, axisX: 1f);
        Pump(4);

        // Limitado a MaxInputDelta (0,05 s) = 10 px, não 2000.
        Assert.Equal(Speed * client.Sync.MaxInputDelta, host.XOf(netId) - antes, 0.5f);
    }

    [Fact]
    public void EixoForaDoLimiteELimitado()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        ushort netId = SpawnPlayer(host, client.Session.SelfId);
        float antes = host.XOf(netId);

        SendRawInput(client, sequence: 50_000, deltaTime: Step, axisX: 100f);
        Pump(4);

        Assert.Equal(Speed * Step, host.XOf(netId) - antes, 0.5f);
    }

    [Fact]
    public void CadaJogadorSoMoveOProprioBoneco()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump(6);

        ushort idAna = SpawnPlayer(host, ana.Session.SelfId);
        ushort idBruno = SpawnPlayer(host, bruno.Session.SelfId);

        ana.Input = new NetInputState(1f, 0f, 0u);
        Pump(30);
        ana.Input = default;
        Pump(6);

        Assert.True(host.XOf(idAna) > 50f, "boneco da Ana não andou");
        Assert.Equal(0f, host.XOf(idBruno), Tolerance);
    }

    [Fact]
    public void JogadorNovoComIdReaproveitadoNaoFicaTravado()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump(4);

        ushort idAna = SpawnPlayer(host, ana.Session.SelfId);

        // Ana joga um tempo — a fila de input dela chega a números altos.
        ana.Input = new NetInputState(1f, 0f, 0u);
        Pump(60);
        ana.Input = default;
        Pump(4);

        ana.Session.Leave();
        host.Sync.Despawn(idAna);
        Pump(4);

        // Bruno entra e recebe o id 1, que era da Ana. Se a fila velha tivesse sobrado, os
        // inputs dele (numerados do 1) seriam todos descartados como "já processados".
        var bruno = CreateClient("Bruno");
        Pump(6);

        Assert.Equal(1, bruno.Session.SelfId);

        ushort idBruno = SpawnPlayer(host, bruno.Session.SelfId);
        float inicio = host.XOf(idBruno);

        bruno.Input = new NetInputState(1f, 0f, 0u);
        Pump(30);
        bruno.Input = default;
        Pump(6);

        Assert.True(host.XOf(idBruno) > inicio + 50f, "input do jogador novo foi descartado");
    }

    [Fact]
    public void ContadorDeInputPendenteMostraSaude()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump(4);

        SpawnPlayer(host, client.Session.SelfId);

        client.Input = new NetInputState(1f, 0f, 0u);
        Pump(30);

        // Rede saudável: o host confirma quase tudo, sobra pouca coisa por confirmar.
        Assert.True(client.Sync.PendingInputCount <= 6, $"pendentes: {client.Sync.PendingInputCount}");
        Assert.True(client.Sync.LastAcknowledgedInput > 0);

        // Host sumiu: o cliente continua prevendo e a fila cresce — é o sinal de rede ruim.
        _net.PacketLoss = 1f;
        Pump(30);

        Assert.True(client.Sync.PendingInputCount > 10, $"pendentes: {client.Sync.PendingInputCount}");
    }

    [Fact]
    public void BonecoDoOutroJogadorContinuaInterpolado()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump(6);

        ushort idAna = SpawnPlayer(host, ana.Session.SelfId);

        ana.Input = new NetInputState(1f, 0f, 0u);
        Pump(40);
        ana.Input = default;
        Pump(10);

        // O Bruno não simula o boneco da Ana: ele reproduz o que o host mandou.
        Assert.False(bruno.FindByNetId(idAna)!.Value.Get<NetworkIdentity>()!.IsMine);
        Assert.Equal(host.XOf(idAna), bruno.XOf(idAna), 1f);
    }

    private static void SendRawInput(Machine client, uint sequence, float deltaTime, float axisX)
    {
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Input);
        writer.WriteByte(1);
        writer.WriteUInt32(sequence);
        writer.WriteSingle(deltaTime);
        writer.WriteSingle(axisX);
        writer.WriteSingle(0f);
        writer.WriteUInt32(0u);

        client.Session.Client!.Send(writer.Written);
    }
}
