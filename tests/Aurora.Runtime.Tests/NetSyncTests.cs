using System.Net;
using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Sincronização de entidades entre máquinas: host e clientes com <see cref="World"/>
/// separados, conversando por <see cref="LoopbackNetwork"/>. É o teste que responde "o boneco
/// do outro jogador aparece e se mexe na minha tela?".
/// </summary>
public class NetSyncTests : IDisposable
{
    private const int HostPort = 7777;
    private const byte PlayerPrefab = 1;
    private const float Tolerance = 0.01f;

    /// <summary>Um passo de rede. 0,05 s = exatamente o intervalo de snapshot padrão (20 Hz),
    /// então cada passo rende um pacote.</summary>
    private const float Step = 0.05f;

    private readonly LoopbackNetwork _net = new();
    private readonly List<Machine> _machines = [];

    /// <summary>Uma máquina: mundo próprio, sessão própria, sincronização própria — do mesmo
    /// jeito que dois PCs na mesma sala.</summary>
    private sealed class Machine
    {
        public required World World { get; init; }
        public required NetSession Session { get; init; }
        public required NetSyncSystem Sync { get; init; }

        public Entity? FindByNetId(ushort netId)
        {
            foreach (var (entity, _, identity) in World.Query<Transform, NetworkIdentity>())
            {
                if (identity.NetId == netId) return entity;
            }

            return null;
        }

        public Vector2 PositionOf(ushort netId)
            => FindByNetId(netId)?.Get<Transform>()?.Position ?? throw new InvalidOperationException($"NetId {netId} não existe nesta máquina.");

        public int SyncedEntityCount => World.Query<Transform, NetworkIdentity>().Count();
    }

    private Machine CreateMachine()
    {
        var world = new World();
        var session = new NetSession();
        var sync = session.AttachWorld(world);

        // Interpolação desligada: os testes verificam O QUE chega, não o amaciamento visual —
        // isso é assunto do NetInterpolatorTests. Com atraso, toda asserção de posição teria
        // que carregar um deslocamento de tempo no meio e esconderia o que está sendo testado.
        sync.InterpolationDelay = 0f;

        sync.Prefabs.Register(PlayerPrefab, static (world, identity) =>
        {
            var entity = world.CreateEntity($"Player{identity.OwnerId}");
            entity.Add(new Transform());
            return entity;
        });

        var machine = new Machine { World = world, Session = session, Sync = sync };
        _machines.Add(machine);
        return machine;
    }

    private Machine CreateHost(int maxPlayers = NetProtocol.MaxPlayersLimit)
    {
        var machine = CreateMachine();
        machine.Session.StartHost(new NetHost(_net.CreateTransport(HostPort), "Host", maxPlayers));
        return machine;
    }

    private Machine CreateClient(string name)
    {
        var machine = CreateMachine();
        var client = machine.Session.Join(new NetClient(_net.CreateTransport()));
        client.Connect(new IPEndPoint(IPAddress.Loopback, HostPort), name);
        return machine;
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

    [Fact]
    public void HostNumeraAsEntidadesDaCena()
    {
        var host = CreateHost();

        var entity = host.World.CreateEntity("Caixote");
        entity.Add(new Transform(10f, 20f));
        var identity = entity.Add(new NetworkIdentity());

        Assert.Equal(0, identity.NetId);

        Pump();

        Assert.NotEqual(0, identity.NetId);
        Assert.True(identity.IsMine);
    }

    [Fact]
    public void ClienteRecebeOBonecoQueOHostCriou()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var spawned = host.Sync.Spawn(PlayerPrefab, ownerId: NetProtocol.HostId);
        spawned.Get<Transform>()!.Position = new Vector2(64f, 32f);
        ushort netId = spawned.Get<NetworkIdentity>()!.NetId;

        Pump();

        var remote = client.FindByNetId(netId);
        Assert.NotNull(remote);
        Assert.Equal(64f, client.PositionOf(netId).X, Tolerance);
        Assert.Equal(32f, client.PositionOf(netId).Y, Tolerance);

        // O boneco é do host, não deste jogador.
        Assert.False(remote!.Value.Get<NetworkIdentity>()!.IsMine);
    }

    [Fact]
    public void MovimentoDoHostChegaNoCliente()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, ownerId: NetProtocol.HostId);
        var transform = boneco.Get<Transform>()!;
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        Pump();

        for (int i = 1; i <= 10; i++)
        {
            transform.Position = new Vector2(i * 10f, 0f);
            Pump(1);
        }

        Pump(2);

        Assert.Equal(100f, client.PositionOf(netId).X, Tolerance);
    }

    [Fact]
    public void ClienteControlaOProprioBonecoEOHostVe()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        byte clientId = client.Session.SelfId;
        Assert.Equal(1, clientId);

        var boneco = host.Sync.Spawn(PlayerPrefab, ownerId: clientId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        Pump();

        var local = client.FindByNetId(netId);
        Assert.NotNull(local);
        Assert.True(local!.Value.Get<NetworkIdentity>()!.IsMine);

        // Quem manda no boneco é o dono: o cliente move localmente e transmite.
        local.Value.Get<Transform>()!.Position = new Vector2(-40f, 15f);
        Pump();

        Assert.Equal(-40f, host.PositionOf(netId).X, Tolerance);
        Assert.Equal(15f, host.PositionOf(netId).Y, Tolerance);
    }

    [Fact]
    public void EntidadeDoJogadorNaoEEsmagadaPeloSnapshot()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, ownerId: client.Session.SelfId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        Pump();

        var localTransform = client.FindByNetId(netId)!.Value.Get<Transform>()!;

        // O host reenvia a posição de todo mundo, inclusive a nossa. Se o snapshot fosse
        // aplicado por cima do boneco do próprio jogador, ele andaria e voltaria a cada
        // pacote — o efeito de elástico clássico.
        for (int i = 1; i <= 8; i++)
        {
            localTransform.Position = new Vector2(i * 5f, 0f);
            Pump(1);
        }

        Assert.Equal(40f, localTransform.Position.X, Tolerance);
    }

    [Fact]
    public void ClienteNaoConsegueMexerNoBonecoDeOutro()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var doHost = host.Sync.Spawn(PlayerPrefab, ownerId: NetProtocol.HostId);
        doHost.Get<Transform>()!.Position = new Vector2(100f, 100f);
        ushort netId = doHost.Get<NetworkIdentity>()!.NetId;
        Pump();

        // Cliente adulterado: marca como sua uma entidade que é do host e passa a transmitir
        // uma posição inventada pra ela.
        var copia = client.FindByNetId(netId)!.Value;
        copia.Get<NetworkIdentity>()!.IsMine = true;
        copia.Get<Transform>()!.Position = new Vector2(-999f, -999f);
        Pump();

        Assert.Equal(100f, host.PositionOf(netId).X, Tolerance);
        Assert.Equal(100f, host.PositionOf(netId).Y, Tolerance);
    }

    [Fact]
    public void EntidadeDestruidaNoHostSomeNosClientes()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, ownerId: NetProtocol.HostId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        Pump();

        Assert.NotNull(client.FindByNetId(netId));

        host.Sync.Despawn(netId);
        Pump();

        Assert.Null(client.FindByNetId(netId));
        Assert.Equal(0, client.SyncedEntityCount);
    }

    [Fact]
    public void EntidadeDestruidaPeloJogoTambemSomeNosClientes()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, ownerId: NetProtocol.HostId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        Pump();

        // Sem passar pelo Despawn: o jogo destruiu a entidade do jeito de sempre.
        boneco.Destroy();
        Pump();

        Assert.Null(client.FindByNetId(netId));
    }

    [Fact]
    public void SairDaSalaLimpaOsBonecosQueVieramDaRede()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        host.Sync.Spawn(PlayerPrefab, ownerId: NetProtocol.HostId);
        host.Sync.Spawn(PlayerPrefab, ownerId: client.Session.SelfId);
        Pump();

        Assert.Equal(2, client.SyncedEntityCount);

        client.Session.Leave();
        client.Session.Update(Step);

        Assert.Equal(0, client.SyncedEntityCount);
        Assert.Equal(0, client.Sync.SyncedCount);
    }

    [Fact]
    public void PrefabDesconhecidoEIgnoradoEmVezDeDerrubarAPartida()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        // Host cria com uma receita que o cliente não conhece (build desatualizado).
        host.Sync.Prefabs.Register(99, static (world, identity) =>
        {
            var entity = world.CreateEntity("Novidade");
            entity.Add(new Transform());
            return entity;
        });

        host.Sync.Spawn(99, ownerId: NetProtocol.HostId);
        Pump();

        Assert.Equal(1, host.Sync.SyncedCount);
        Assert.Equal(0, client.SyncedEntityCount);
    }

    [Fact]
    public void SnapshotAtrasadoNaoFazOBonecoVoltar()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, ownerId: NetProtocol.HostId);
        var transform = boneco.Get<Transform>()!;
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;

        transform.Position = new Vector2(200f, 0f);
        Pump(4);

        Assert.Equal(200f, client.PositionOf(netId).X, Tolerance);

        // Pacote reordenado pela rede: mesma entidade, posição antiga, sequência 1 (bem mais
        // velha que a que já foi aplicada). Tem que ser descartado.
        Span<byte> buffer = stackalloc byte[NetProtocol.MaxPacketSize];
        var writer = new NetWriter(buffer, NetMessageType.Snapshot);
        writer.WriteUInt16(1);
        writer.WriteByte(1);
        writer.WriteUInt16(netId);
        writer.WriteByte(NetProtocol.HostId);
        writer.WriteByte(PlayerPrefab);
        writer.WriteSingle(-500f);
        writer.WriteSingle(-500f);
        writer.WriteSingle(0f);

        var peer = host.Session.Host!.Peers[1];
        host.Session.Host!.SendTo(peer, writer.Written);
        client.Session.Update(Step);

        Assert.Equal(200f, client.PositionOf(netId).X, Tolerance);
    }

    [Fact]
    public void TresJogadoresEnxergamOsTresBonecos()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump();

        var doHost = host.Sync.Spawn(PlayerPrefab, NetProtocol.HostId);
        var daAna = host.Sync.Spawn(PlayerPrefab, ana.Session.SelfId);
        var doBruno = host.Sync.Spawn(PlayerPrefab, bruno.Session.SelfId);

        ushort idHost = doHost.Get<NetworkIdentity>()!.NetId;
        ushort idAna = daAna.Get<NetworkIdentity>()!.NetId;
        ushort idBruno = doBruno.Get<NetworkIdentity>()!.NetId;
        Pump();

        // Cada um move o seu.
        doHost.Get<Transform>()!.Position = new Vector2(0f, 0f);
        ana.FindByNetId(idAna)!.Value.Get<Transform>()!.Position = new Vector2(50f, 0f);
        bruno.FindByNetId(idBruno)!.Value.Get<Transform>()!.Position = new Vector2(100f, 0f);
        Pump(6);

        foreach (var machine in new[] { host, ana, bruno })
        {
            Assert.Equal(3, machine.SyncedEntityCount);
            Assert.Equal(0f, machine.PositionOf(idHost).X, Tolerance);
            Assert.Equal(50f, machine.PositionOf(idAna).X, Tolerance);
            Assert.Equal(100f, machine.PositionOf(idBruno).X, Tolerance);
        }
    }

    [Fact]
    public void SpawnEDespawnSaoExclusivosDoHost()
    {
        CreateHost();
        var client = CreateClient("Ana");
        Pump();

        Assert.Throws<InvalidOperationException>(() => client.Sync.Spawn(PlayerPrefab, 1));
        Assert.Throws<InvalidOperationException>(() => client.Sync.Despawn(1));
    }

    [Fact]
    public void PrefabIdZeroEReservado()
    {
        var host = CreateHost();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => host.Sync.Prefabs.Register(0, static (world, _) => world.CreateEntity()));
    }

    [Fact]
    public void JogoOfflineNaoSincronizaNada()
    {
        var machine = CreateMachine();

        var entity = machine.World.CreateEntity("Solo");
        entity.Add(new Transform(5f, 5f));
        entity.Add(new NetworkIdentity());

        machine.Session.Update(Step);

        Assert.Equal(0, machine.Sync.SyncedCount);
        Assert.Equal(5f, entity.Get<Transform>()!.Position.X, Tolerance);
    }
}
