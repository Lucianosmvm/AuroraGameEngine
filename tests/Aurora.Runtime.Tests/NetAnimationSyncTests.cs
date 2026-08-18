using System.Net;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Net;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Clipe de animação no snapshot. Sem isso o boneco dos outros jogadores atravessa o mapa
/// deslizando na pose de descanso, porque a posição chega pela rede mas o
/// <see cref="Animator"/> deles nunca sai do lugar.
/// </summary>
public class NetAnimationSyncTests : IDisposable
{
    private const int HostPort = 7777;
    private const byte PlayerPrefab = 1;
    private const byte SemAnimacaoPrefab = 2;
    private const float Step = 0.05f;

    private readonly LoopbackNetwork _net = new();
    private readonly List<NetSession> _sessions = [];

    private sealed record Machine(World World, NetSession Session, NetSyncSystem Sync)
    {
        public Animator? AnimatorOf(ushort netId)
        {
            foreach (var (entity, _, identity) in World.Query<Transform, NetworkIdentity>())
            {
                if (identity.NetId == netId) return entity.Get<Animator>();
            }

            return null;
        }
    }

    /// <summary>Boneco com dois clipes. Os frames precisam existir: <see cref="Animator.Play"/>
    /// ignora clipe vazio, e um teste montado assim passaria sem provar nada.</summary>
    private static Entity CriarBoneco(World world, NetworkIdentity identity)
    {
        var entity = world.CreateEntity($"Player{identity.OwnerId}");
        entity.Add(new Transform());
        entity.Add(new Animator
        {
            FrameWidth = 16,
            FrameHeight = 16,
            SheetColumns = 4,
            Clips =
            [
                new AnimationClip { Name = "parado", Frames = [0] },
                new AnimationClip { Name = "andar", Frames = [1, 2, 3] },
                new AnimationClip { Name = "pular", Frames = [4] },
            ],
        });

        return entity;
    }

    private Machine CreateMachine()
    {
        var world = new World();
        var session = new NetSession();
        var sync = session.AttachWorld(world);
        sync.InterpolationDelay = 0f;

        sync.Prefabs.Register(PlayerPrefab, CriarBoneco);
        sync.Prefabs.Register(SemAnimacaoPrefab, static (world, identity) =>
        {
            var entity = world.CreateEntity("Caixote");
            entity.Add(new Transform());
            return entity;
        });

        _sessions.Add(session);
        return new Machine(world, session, sync);
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

    private void Pump(int steps = 4)
    {
        for (int i = 0; i < steps; i++)
        {
            foreach (var session in _sessions)
                session.Update(Step);
        }
    }

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Dispose();
    }

    [Fact]
    public void ClipeDoHostChegaNoCliente()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, NetProtocol.HostId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        boneco.Get<Animator>()!.Play("andar");
        Pump();

        Assert.Equal("andar", client.AnimatorOf(netId)?.CurrentClip);
    }

    [Fact]
    public void TrocaDeClipePropaga()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, NetProtocol.HostId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        var animator = boneco.Get<Animator>()!;

        animator.Play("andar");
        Pump();
        Assert.Equal("andar", client.AnimatorOf(netId)?.CurrentClip);

        animator.Play("pular");
        Pump();
        Assert.Equal("pular", client.AnimatorOf(netId)?.CurrentClip);

        animator.Play("parado");
        Pump();
        Assert.Equal("parado", client.AnimatorOf(netId)?.CurrentClip);
    }

    [Fact]
    public void ClipeNasceCertoNaCriacaoDaEntidade()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, NetProtocol.HostId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;

        // Já andando quando o cliente vê a entidade pela primeira vez: sem aplicar o clipe no
        // nascimento, ele apareceria parado até a próxima troca.
        boneco.Get<Animator>()!.Play("andar");
        Pump();

        Assert.Equal("andar", client.AnimatorOf(netId)?.CurrentClip);
    }

    [Fact]
    public void AnimacaoDoClienteChegaNoHostENosOutros()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        var bruno = CreateClient("Bruno");
        Pump(6);

        var boneco = host.Sync.Spawn(PlayerPrefab, ana.Session.SelfId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        Pump();

        // Quem decide a animação do boneco é a máquina dona dele.
        ana.AnimatorOf(netId)!.Play("andar");
        Pump(4);

        Assert.Equal("andar", host.AnimatorOf(netId)?.CurrentClip);
        Assert.Equal("andar", bruno.AnimatorOf(netId)?.CurrentClip);
    }

    [Fact]
    public void ClipeDoProprioBonecoNaoEImpostoPeloHost()
    {
        var host = CreateHost();
        var ana = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, ana.Session.SelfId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        Pump();

        // Host acha que ela está parada; a máquina dela diz que está pulando. Quem manda no
        // próprio boneco é ela — impor o clipe do host faria a animação piscar a 20 Hz.
        host.AnimatorOf(netId)!.Play("parado");
        ana.AnimatorOf(netId)!.Play("pular");
        Pump(4);

        Assert.Equal("pular", ana.AnimatorOf(netId)?.CurrentClip);
    }

    [Fact]
    public void EntidadeSemAnimatorNaoQuebraNada()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var caixote = host.Sync.Spawn(SemAnimacaoPrefab, NetProtocol.HostId);
        ushort netId = caixote.Get<NetworkIdentity>()!.NetId;
        caixote.Get<Transform>()!.Position = new System.Numerics.Vector2(10f, 5f);
        Pump();

        Assert.Null(client.AnimatorOf(netId));
        Assert.Equal(1, client.Sync.SyncedCount);
    }

    [Fact]
    public void ClipeQueNaoExisteNaOutraMaquinaEIgnorado()
    {
        var host = CreateHost();
        var client = CreateClient("Ana");
        Pump();

        var boneco = host.Sync.Spawn(PlayerPrefab, NetProtocol.HostId);
        ushort netId = boneco.Get<NetworkIdentity>()!.NetId;
        boneco.Get<Animator>()!.Play("andar");
        Pump();

        // Cliente com o boneco de um build antigo, sem os clipes extras. O índice que chega
        // não existe na lista dele — ignorar é o certo, derrubar a partida não.
        var animatorDoCliente = client.AnimatorOf(netId)!;
        animatorDoCliente.Clips.RemoveRange(1, animatorDoCliente.Clips.Count - 1);

        boneco.Get<Animator>()!.Play("pular");
        Pump();

        Assert.Equal("andar", animatorDoCliente.CurrentClip);
        Assert.Equal(1, client.Sync.SyncedCount);
    }
}
