using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Chamar a montaria de longe: ela larga o pasto, vem até o dono e volta a pastar onde parou.
/// O que se prende aqui são as três transições de estado (pastando → vindo → parada) e as
/// saídas sujas, que é onde esse tipo de mecânica trava o bicho num limbo.
/// </summary>
public class MountCallTests
{
    private const float Tolerance = 0.01f;

    private static void Advance(World world, int frames, float step = 1f / 60f)
    {
        for (int i = 0; i < frames; i++)
            world.Update(step);
    }

    /// <summary>
    /// Roda até a montaria terminar a vinda, e devolve onde ela parou.
    ///
    /// <para>Medir a posição N frames depois seria errado: assim que chega, ela volta a pastar e
    /// se afasta de novo dentro do raio. A distância só quer dizer alguma coisa no instante da
    /// chegada.</para>
    /// </summary>
    private static Vector2 AdvanceUntilArrived(World world, Rideable ride, Entity mount, int maxFrames = 900)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            world.Update(1f / 60f);
            if (!ride.IsComing)
                break;
        }

        Assert.False(ride.IsComing, "Nunca terminou a vinda.");
        return mount.Get<Transform>()!.Position;
    }

    /// <summary>Dono na origem, cavalo longe, pastando e atendendo assobio.</summary>
    private static (World World, Entity Rider, Entity Horse, Rideable Ride, Wander Wander) Build(
        float distance = 600f, float callRange = 0f, bool withNavAgent = false)
    {
        var world = new World();

        var rider = world.CreateEntity("Player");
        rider.Add(new Transform());
        rider.Add(new TopDownController { UseKeyboard = false });

        var horse = world.CreateEntity("Cavalo");
        horse.Add(new Transform(new Vector2(distance, 0f)));
        horse.Add(new SpriteRenderer());
        horse.Add(new TopDownController { UseKeyboard = false });

        if (withNavAgent)
            horse.Add(new NavAgent { Speed = 150f });

        var wander = new Wander { Radius = 60f, Speed = 30f, PauseMin = 0f, PauseMax = 0.05f };
        horse.Add(wander);

        var ride = new Rideable
        {
            CallKey = "",              // chamado por API nos testes
            CallRange = callRange,
            CallSpeed = 200f,
            CallArriveDistance = 30f,
        };
        horse.Add(ride);

        world.Update(1f / 60f);
        return (world, rider, horse, ride, wander);
    }

    [Fact]
    public void ChamadoOCavaloVemAteODono()
    {
        var (world, _, horse, ride, _) = Build(distance: 600f);

        Assert.True(ride.Call());
        var chegada = AdvanceUntilArrived(world, ride, horse);

        Assert.True(chegada.Length() <= 31f, $"Parou longe do dono: {chegada}");
    }

    [Fact]
    public void EnquantoVemOPastoFicaCalado()
    {
        // Sem isto, o destino do pasto briga com o do chamado e o cavalo fica indo e voltando.
        var (_, _, _, ride, wander) = Build();

        ride.Call();

        Assert.True(ride.IsComing);
        Assert.False(wander.Enabled);
    }

    [Fact]
    public void AoChegarVoltaAPastar()
    {
        var (world, _, _, ride, wander) = Build(distance: 300f);
        ride.Call();

        Advance(world, 300);

        Assert.False(ride.IsComing, "Ficou preso no estado de vinda.");
        Assert.True(wander.Enabled, "Não voltou a pastar.");
    }

    [Fact]
    public void PastaOndeParouENaoVoltaProNascimento()
    {
        // O bug que este ajuste corrige: o lar do Wander é fixado no Start, então sem refixar o
        // cavalo atravessaria o mapa de volta pro ponto onde nasceu pra "pastar" longe de todos.
        var (world, _, horse, ride, wander) = Build(distance: 800f);

        ride.Call();

        // AdvanceUntilArrived, não um número fixo de frames: assim que chega, o cavalo refixa o
        // lar e JÁ COMEÇA a pastar, sorteando alvo dentro do raio. Com "Advance(world, 400)" a
        // medida caía ora no instante da chegada, ora alguns passos de pasto depois — e aí
        // "onde parou" já vinha deslocado, deixando o teste falhar em ~40% das execuções.
        var ondeParou = AdvanceUntilArrived(world, ride, horse);
        Advance(world, 900);           // 15s pastando

        float distanciaDoNascimento = Vector2.Distance(horse.Get<Transform>()!.Position, new Vector2(800f, 0f));
        Assert.True(distanciaDoNascimento > 400f,
            $"Voltou andando pro ponto de nascimento: está a {distanciaDoNascimento}px dele.");

        Assert.True(Vector2.Distance(horse.Get<Transform>()!.Position, ondeParou) <= wander.Radius + 5f,
            "Saiu do raio em volta de onde parou.");
    }

    [Fact]
    public void ForaDeAlcanceNaoAtende()
    {
        var (_, _, _, ride, wander) = Build(distance: 2000f, callRange: 500f);

        Assert.False(ride.Call());
        Assert.False(ride.IsComing);
        Assert.True(wander.Enabled, "Calou o pasto mesmo sem atender.");
    }

    [Fact]
    public void AlcanceZeroChamaDoMapaInteiro()
    {
        var (_, _, _, ride, _) = Build(distance: 9000f, callRange: 0f);

        Assert.True(ride.Call());
    }

    [Fact]
    public void MontarCancelaAVinda()
    {
        var (world, _, horse, ride, _) = Build(distance: 20f);
        ride.Call();

        Assert.True(ride.TryMount());

        Assert.False(ride.IsComing, "Continuou 'vindo' com o dono em cima.");
    }

    [Fact]
    public void UsaONavAgentQuandoExisteParaContornarParede()
    {
        var (world, _, horse, ride, _) = Build(distance: 600f, withNavAgent: true);

        ride.Call();
        var chegada = AdvanceUntilArrived(world, ride, horse);

        Assert.True(chegada.Length() <= 31f, $"Não veio pelo NavAgent: {chegada}");
    }

    [Fact]
    public void OCavaloAcompanhaODonoQueSeMoveEnquantoEleVem()
    {
        // Reapontar de tempos em tempos é o que evita ele correr pro ponto onde você ESTAVA.
        var (world, rider, horse, ride, _) = Build(distance: 600f);
        ride.Call();

        Advance(world, 60);
        rider.Get<Transform>()!.Position = new Vector2(0f, 900f);   // dono anda pro lado

        var chegada = AdvanceUntilArrived(world, ride, horse);

        Assert.True(Vector2.Distance(chegada, new Vector2(0f, 900f)) <= 31f,
            $"Foi pro lugar antigo: {chegada}");
    }

    [Fact]
    public void DonoQueSaiDaCenaInterrompeAVinda()
    {
        // Sem isso a montaria andaria pro último ponto conhecido e ficaria presa no estado.
        var (world, rider, _, ride, wander) = Build(distance: 600f);
        ride.Call();

        rider.Destroy();
        Advance(world, 5);

        Assert.False(ride.IsComing);
        Assert.True(wander.Enabled, "Ficou sem pastar e sem vir.");
    }

    [Fact]
    public void CancelarAVindaDevolveOPasto()
    {
        var (_, _, _, ride, wander) = Build();
        ride.Call();

        ride.StopComing();

        Assert.False(ride.IsComing);
        Assert.True(wander.Enabled);
    }

    [Fact]
    public void ChamarMontadoNaoFazNada()
    {
        var (_, _, _, ride, _) = Build(distance: 20f);
        ride.TryMount();

        Assert.False(ride.Call());
        Assert.False(ride.IsComing);
    }
}
