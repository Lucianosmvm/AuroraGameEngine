using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Cutscene básica no estilo RPG Maker: personagem anda até um ponto, PARA a sequência até
/// chegar, e só então mostra a mensagem (com retrato opcional). O que se prende aqui é o
/// travamento — MoveTo tem que pausar a sequência igual ShowMessage já pausa, senão "anda até
/// ali e fala" viraria "anda e fala ao mesmo tempo", que não é cutscene nenhuma.
/// </summary>
public class CutsceneTests
{
    private const float Tolerance = 0.5f;

    private static (World World, EventSystem Events, GameState State) Build()
    {
        var world = new World();
        var state = new GameState();
        return (world, new EventSystem(world, state), state);
    }

    private static Entity WithTrigger(World world, string name, Vector2 position, params EventAction[] actions)
    {
        var entity = world.CreateEntity(name);
        entity.Add(new Transform(position));
        entity.Add(new EventTrigger { Trigger = "SceneStart", Once = true, Actions = [.. actions] });
        return entity;
    }

    /// <summary>Roda frames suficientes pra qualquer sequência desta suíte terminar, ou estoura —
    /// preferível a um teste que trava o runner se o bloqueio nunca soltar.</summary>
    private static void RunUntilIdle(World world, EventSystem events, int maxFrames = 300)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            world.Update(0.05f);
            events.Update(0.05f);
        }
    }

    [Fact]
    public void MoveToParaASequenciaAteChegar()
    {
        var (world, events, state) = Build();
        var ator = WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 200f, Y = 0f },
            new EventAction { Type = "SetSwitch", Name = "Chegou", On = true });

        // Só a primeira ação teve chance de rodar num frame — o Set do switch ainda não.
        world.Update(0.05f);
        events.Update(0.05f);
        Assert.False(state.GetSwitch("Chegou"));
        Assert.True(ator.Get<NavAgent>()!.IsMoving);

        RunUntilIdle(world, events);

        Assert.True(state.GetSwitch("Chegou"));
        Assert.Equal(200f, ator.Get<Transform>()!.Position.X, Tolerance);
        Assert.False(ator.Get<NavAgent>()!.IsMoving);
    }

    [Fact]
    public void MoveToCriaNavAgentSozinhoEmQuemNaoTem()
    {
        var (world, events, _) = Build();
        var ator = WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 50f, Y = 0f });

        Assert.Null(ator.Get<NavAgent>());

        world.Update(0.05f);
        events.Update(0.05f);

        Assert.NotNull(ator.Get<NavAgent>());
    }

    [Fact]
    public void ValorMaiorQueZeroSubstituiAVelocidade()
    {
        var (world, events, _) = Build();
        var ator = WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 1000f, Y = 0f, Value = 500f });

        world.Update(0.05f);
        events.Update(0.05f);

        Assert.Equal(500f, ator.Get<NavAgent>()!.Speed, Tolerance);
    }

    [Fact]
    public void ValorZeroMantemAVelocidadeJaConfigurada()
    {
        var (world, events, _) = Build();
        var ator = WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 1000f, Y = 0f });
        ator.Add(new NavAgent { Speed = 77f });

        world.Update(0.05f);
        events.Update(0.05f);

        Assert.Equal(77f, ator.Get<NavAgent>()!.Speed, Tolerance);
    }

    [Fact]
    public void MoveToReligaNavAgentDesligado()
    {
        // Rideable desliga o NavAgent do cavalo enquanto alguém monta; uma cutscene que chama
        // MoveTo tem que valer mesmo assim, senão "IA desligada por engano" vira bug silencioso.
        var (world, events, _) = Build();
        var ator = WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 50f, Y = 0f });
        ator.Add(new NavAgent { Enabled = false });

        world.Update(0.05f);
        events.Update(0.05f);

        Assert.True(ator.Get<NavAgent>()!.Enabled);
    }

    [Fact]
    public void SelfMoveComQuemDisparouOEvento()
    {
        var (world, events, state) = Build();
        var ator = WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 30f, Y = 0f }, // Name nulo = Self
            new EventAction { Type = "SetSwitch", Name = "Chegou", On = true });

        RunUntilIdle(world, events);

        Assert.True(state.GetSwitch("Chegou"));
        Assert.Equal(30f, ator.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void JaEstarNoDestinoNaoTravaACutscene()
    {
        var (world, events, state) = Build();
        WithTrigger(world, "Ator", new Vector2(10f, 10f),
            new EventAction { Type = "MoveTo", X = 10f, Y = 10f },
            new EventAction { Type = "SetSwitch", Name = "Chegou", On = true });

        RunUntilIdle(world, events);

        Assert.True(state.GetSwitch("Chegou"));
    }

    [Fact]
    public void DestinoBloqueadoDesistemEmVezDeTravarParaSempre()
    {
        // Um anel de tiles sólidos ao redor do alvo torna o destino inalcançável — o pathfinder
        // devolve null, e MoveTo tem que desistir (não travar a cutscene o resto do jogo).
        var (world, events, state) = Build();

        var mapa = world.CreateEntity("Mapa");
        mapa.Add(new Transform(Vector2.Zero));
        var tiles = new int[10 * 10];
        for (int i = 0; i < tiles.Length; i++) tiles[i] = -1;
        foreach (var (x, y) in new[] { (4, 4), (5, 4), (6, 4), (4, 5), (6, 5), (4, 6), (5, 6), (6, 6) })
            tiles[y * 10 + x] = 0;
        mapa.Add(new Tilemap
        {
            Width = 10,
            Height = 10,
            TileWidth = 16,
            TileHeight = 16,
            SolidTiles = [0],
            Tiles = tiles,
        });

        WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 5 * 16 + 8, Y = 5 * 16 + 8 },
            new EventAction { Type = "SetSwitch", Name = "Terminou", On = true });

        RunUntilIdle(world, events);

        Assert.True(state.GetSwitch("Terminou"));
    }

    [Fact]
    public void EntidadeDestruidaNoMeioDoTrajetoDesbloqueiaACutscene()
    {
        var (world, events, state) = Build();
        var ator = WithTrigger(world, "Ator", Vector2.Zero,
            new EventAction { Type = "MoveTo", X = 1000f, Y = 0f, Value = 1f }, // bem devagar
            new EventAction { Type = "SetSwitch", Name = "NuncaChega", On = true });

        world.Update(0.05f);
        events.Update(0.05f);
        Assert.True(ator.Get<NavAgent>()!.IsMoving);

        ator.Destroy();
        RunUntilIdle(world, events);

        Assert.False(state.GetSwitch("NuncaChega"));
    }

    // ---------- Retrato na mensagem ----------

    [Fact]
    public void ShowMessageCarregaORetratoAteODialogo()
    {
        var (world, events, _) = Build();
        var dialogue = new DialogueSystem();
        events.Dialogue = dialogue;

        WithTrigger(world, "Npc", Vector2.Zero,
            new EventAction { Type = "ShowMessage", Text = "Bem-vindo!", Name = "Ferreiro", Portrait = "sprites/ferreiro.png" });

        world.Update(0.05f);
        events.Update(0.05f);
        dialogue.Update();

        var message = Assert.IsType<DialogueMessage>(dialogue.Current);
        Assert.Equal("Bem-vindo!", message.Text);
        Assert.Equal("Ferreiro", message.Speaker);
        Assert.Equal("sprites/ferreiro.png", message.Portrait);
    }

    [Fact]
    public void ShowMessageSemPortraitContinuaFuncionando()
    {
        var dialogue = new DialogueSystem();
        dialogue.ShowMessage("Oi.", "Guarda");
        dialogue.Update();

        var message = Assert.IsType<DialogueMessage>(dialogue.Current);
        Assert.Null(message.Portrait);
    }

    // ---------- Round-trip de cena ----------

    [Fact]
    public void MoveToEPortraitSobrevivemAoSalvarECarregarACena()
    {
        var serializer = new SceneSerializer();
        var world = new World();
        var entity = world.CreateEntity("Npc");
        entity.Add(new Transform(Vector2.Zero));
        entity.Add(new EventTrigger
        {
            Trigger = "PlayerInteract",
            Actions =
            [
                new EventAction { Type = "MoveTo", X = 64f, Y = 32f, Value = 80f },
                new EventAction { Type = "ShowMessage", Text = "Olá!", Name = "Npc", Portrait = "sprites/npc.png" },
            ],
        });

        string json = serializer.Save("Teste", new SceneContext { World = world });

        var reloaded = new World();
        serializer.Load(json, new SceneContext { World = reloaded });

        var actions = reloaded.Entities.Single(e => e.Name == "Npc").Get<EventTrigger>()!.Actions;
        var moveTo = actions.Single(a => a.Type == "MoveTo");
        var showMessage = actions.Single(a => a.Type == "ShowMessage");

        Assert.Equal(64f, moveTo.X, 0.01f);
        Assert.Equal(32f, moveTo.Y, 0.01f);
        Assert.Equal(80f, moveTo.Value, 0.01f);
        Assert.Equal("sprites/npc.png", showMessage.Portrait);
    }
}
