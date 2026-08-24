using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Perambular sozinho, e a convivência disso com o <see cref="Rideable"/> — o cavalo pasta
/// quando está solto e obedece quando alguém monta. É onde as duas coisas brigariam pelo mesmo
/// Transform se ninguém desligasse uma delas.
/// </summary>
public class WanderTests
{
    private const float Tolerance = 0.01f;

    private static void Advance(World world, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            world.Update(step);
    }

    [Fact]
    public void SaiDoLugarSozinho()
    {
        var world = new World();
        var horse = world.CreateEntity("Cavalo");
        horse.Add(new Transform());
        horse.Add(new Wander { Radius = 100f, Speed = 60f, PauseMin = 0.1f, PauseMax = 0.2f });

        Advance(world, 600);   // 10s

        Assert.True(horse.Get<Transform>()!.Position.Length() > 1f, "Não saiu do lugar.");
    }

    [Fact]
    public void NaoSeAfastaMaisQueORaio()
    {
        // Sem o teto, dez minutos de jogo e a galinha do quintal está do outro lado do mapa.
        var world = new World();
        var chicken = world.CreateEntity("Galinha");
        chicken.Add(new Transform(new Vector2(200f, 100f)));
        chicken.Add(new Wander { Radius = 50f, Speed = 200f, PauseMin = 0f, PauseMax = 0.05f });

        float maisLonge = 0f;
        for (int i = 0; i < 3000; i++)
        {
            world.Update(1f / 60f);
            maisLonge = MathF.Max(maisLonge,
                Vector2.Distance(chicken.Get<Transform>()!.Position, new Vector2(200f, 100f)));
        }

        Assert.True(maisLonge <= 52f, $"Fugiu do raio: {maisLonge}px");
    }

    [Fact]
    public void OCentroEOndeNasceuENaoAOrigemDoMundo()
    {
        // Relativo ao nascimento é o que faz o mesmo prefab de galinha servir a fazenda inteira.
        var world = new World();
        var chicken = world.CreateEntity("Galinha");
        chicken.Add(new Transform(new Vector2(900f, 900f)));
        chicken.Add(new Wander { Radius = 40f, Speed = 100f, PauseMin = 0f, PauseMax = 0.05f });

        Advance(world, 1200);

        Assert.True(Vector2.Distance(chicken.Get<Transform>()!.Position, new Vector2(900f, 900f)) <= 42f);
    }

    [Fact]
    public void ComecaEmPausaSorteadaPraNaoAndarEmBloco()
    {
        // Dez bichos nascidos no mesmo frame dando o primeiro passo juntos parece coreografia,
        // não vida.
        var world = new World();

        var wanderers = new List<Wander>();
        for (int i = 0; i < 12; i++)
        {
            var e = world.CreateEntity($"Bicho{i}");
            e.Add(new Transform());
            var wander = new Wander { PauseMin = 0.5f, PauseMax = 3f };
            e.Add(wander);
            wanderers.Add(wander);
        }

        // Olhar o estado num instante fixo ("depois de 1s, alguém anda e alguém não") depende de
        // sorteio: com pausa em [0.5, 3], os 12 estarem parados em t=1s acontece de vez em
        // quando, e o teste falhava sozinho. O que importa não é o instantâneo — é que eles não
        // deem o primeiro passo TODOS no mesmo frame. Isso dá pra medir sem depender de sorte.
        var primeiroPasso = new int?[wanderers.Count];

        for (int frame = 0; frame < 240; frame++)   // 4s > PauseMax, todos já saíram
        {
            world.Update(1f / 60f);

            for (int i = 0; i < wanderers.Count; i++)
                if (primeiroPasso[i] is null && wanderers[i].IsMoving)
                    primeiroPasso[i] = frame;
        }

        var frames = primeiroPasso.Where(f => f is not null).Select(f => f!.Value).ToList();

        Assert.True(frames.Count >= 2, $"Só {frames.Count} bicho(s) chegou a andar em 4s.");
        Assert.True(frames.Distinct().Count() > 1, "Todos deram o primeiro passo no mesmo frame.");
    }

    [Fact]
    public void UsaONavAgentQuandoExiste()
    {
        // Com NavAgent quem anda é o World (contornando parede); o Wander só decide o destino.
        var world = new World();
        var horse = world.CreateEntity("Cavalo");
        horse.Add(new Transform());
        var agent = new NavAgent { Speed = 80f };
        horse.Add(agent);
        horse.Add(new Wander { Radius = 120f, PauseMin = 0f, PauseMax = 0.05f });

        Advance(world, 120);

        Assert.True(horse.Get<Transform>()!.Position.Length() > 1f,
            "Não andou pelo NavAgent.");
    }

    // ---------- Convivência com o Rideable ----------

    private static (World World, Entity Rider, Entity Horse, Rideable Ride, Wander Wander) BuildMount()
    {
        var world = new World();

        var rider = world.CreateEntity("Player");
        rider.Add(new Transform());
        rider.Add(new TopDownController { UseKeyboard = false });

        var horse = world.CreateEntity("Cavalo");
        horse.Add(new Transform(new Vector2(10f, 0f)));
        horse.Add(new TopDownController { UseKeyboard = false });
        var wander = new Wander { Radius = 100f, Speed = 60f, PauseMin = 0f, PauseMax = 0.05f };
        horse.Add(wander);
        var ride = new Rideable();
        horse.Add(ride);

        world.Update(1f / 60f);
        return (world, rider, horse, ride, wander);
    }

    [Fact]
    public void SoltoOCavaloPasta()
    {
        var (world, _, horse, _, wander) = BuildMount();

        Advance(world, 300);

        Assert.True(wander.Enabled);
        Assert.True(Vector2.Distance(horse.Get<Transform>()!.Position, new Vector2(10f, 0f)) > 1f,
            "O cavalo solto ficou parado.");
    }

    [Fact]
    public void MontadoOCavaloParaDePastar()
    {
        // Se os dois rodassem juntos, o cavalo puxaria pro destino sorteado enquanto o jogador
        // tenta dirigi-lo — o clássico personagem que "anda sozinho".
        var (world, _, horse, ride, wander) = BuildMount();

        ride.TryMount();
        Advance(world, 300);

        Assert.False(wander.Enabled, "A IA continuou rodando com alguém em cima.");

        var parado = horse.Get<Transform>()!.Position;
        Advance(world, 300);

        // Sem input no controlador da montaria, montado ela fica parada.
        Assert.Equal(parado.X, horse.Get<Transform>()!.Position.X, Tolerance);
        Assert.Equal(parado.Y, horse.Get<Transform>()!.Position.Y, Tolerance);
    }

    [Fact]
    public void AoDescerOCavaloVoltaAPastar()
    {
        var (world, _, horse, ride, wander) = BuildMount();
        ride.TryMount();
        Advance(world, 120);

        ride.Dismount();
        var ondeDesceu = horse.Get<Transform>()!.Position;

        Advance(world, 300);

        Assert.True(wander.Enabled);
        Assert.True(Vector2.Distance(horse.Get<Transform>()!.Position, ondeDesceu) > 1f,
            "O cavalo não voltou a andar depois da descida.");
    }

    [Fact]
    public void OCavaloNaoRetomaACaminhadaAntigaAoDescer()
    {
        // Sem o Halt no religar, ele retomaria o destino sorteado antes de você montar e sairia
        // em linha reta de onde quer que a cavalgada tenha terminado.
        var (world, _, horse, ride, wander) = BuildMount();

        Advance(world, 30);            // escolhe um destino solto
        ride.TryMount();

        horse.Get<Transform>()!.Position = new Vector2(3000f, 3000f);   // cavalga pra longe
        Advance(world, 30);
        ride.Dismount();

        // O que não pode é ele DISPARAR pro destino velho: cada frame tem que continuar valendo
        // no máximo um passo de cavalo, nunca um salto.
        float maiorPasso = 0f;
        var anterior = horse.Get<Transform>()!.Position;

        for (int i = 0; i < 240; i++)
        {
            world.Update(1f / 60f);
            var atual = horse.Get<Transform>()!.Position;
            maiorPasso = MathF.Max(maiorPasso, Vector2.Distance(anterior, atual));
            anterior = atual;
        }

        Assert.True(wander.Enabled);
        Assert.True(maiorPasso <= wander.Speed / 60f + 0.1f,
            $"Deu um salto de {maiorPasso}px num frame — retomou a caminhada antiga.");
    }

    [Fact]
    public void NavAgentDesligadoNaoAnda()
    {
        var world = new World();
        var horse = world.CreateEntity("Cavalo");
        horse.Add(new Transform());
        var agent = new NavAgent { Speed = 100f, Enabled = false };
        horse.Add(agent);
        agent.SetTarget(new Vector2(500f, 0f));

        Advance(world, 120);

        Assert.Equal(0f, horse.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void NavAgentReligadoRetomaODestino()
    {
        var world = new World();
        var horse = world.CreateEntity("Cavalo");
        horse.Add(new Transform());
        var agent = new NavAgent { Speed = 100f, Enabled = false };
        horse.Add(agent);
        agent.SetTarget(new Vector2(500f, 0f));

        Advance(world, 60);
        agent.Enabled = true;
        Advance(world, 60);

        Assert.True(horse.Get<Transform>()!.Position.X > 50f, "Não retomou o caminho.");
    }
}
