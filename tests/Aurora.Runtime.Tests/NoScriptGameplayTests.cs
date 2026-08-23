using System.Numerics;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Scenes;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// As pontas que fecham o caminho "jogo sem script": spawn por evento visual, perseguição por
/// NavAgent e movimento do jogador por joystick de toque. Cada uma existe pra apagar um script
/// que antes todo jogo reescrevia.
/// </summary>
public class NoScriptGameplayTests : IDisposable
{
    private const float Tolerance = 0.01f;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-noscript-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ---------- Ação de evento Spawn ----------

    [Fact]
    public void AcaoSpawnInstanciaOPrefabRelativoAQuemDisparou()
    {
        // X/Y como deslocamento é o que faz um mesmo arquivo de prefab servir vários pontos de
        // spawn na cena, sem duplicar o prefab por posição.
        const string prefab = """{ "Name": "Slime", "Components": [ { "Type": "Transform" } ] }""";

        var serializer = new SceneSerializer();
        var world = new World();
        world.PrefabFactory = (path, position) => path == "prefabs/slime.json"
            ? serializer.LoadEntity(prefab, new SceneContext { World = world }, position)
            : null;

        var events = new EventSystem(world, new GameState());

        var spawner = world.CreateEntity("PontoDeSpawn");
        spawner.Add(new Transform(new Vector2(200f, 50f)));
        spawner.Add(new EventTrigger
        {
            Trigger = "SceneStart",
            Actions = [new EventAction { Type = "Spawn", Name = "prefabs/slime.json", X = 10f, Y = -5f }],
        });

        events.Update(0.016f);

        var slime = world.Entities.Single(e => e.Name == "Slime");
        Assert.Equal(210f, slime.Get<Transform>()!.Position.X, Tolerance);
        Assert.Equal(45f, slime.Get<Transform>()!.Position.Y, Tolerance);
    }

    // ---------- NavAgent.Follow ----------

    [Fact]
    public void NavAgentComFollowPerseguALvoQueSeMove()
    {
        var world = new World();
        var player = world.CreateEntity("Player");
        var playerTransform = new Transform(new Vector2(100f, 0f));
        player.Add(playerTransform);

        var enemy = world.CreateEntity("Slime");
        enemy.Add(new Transform());
        enemy.Add(new NavAgent { Speed = 50f, Follow = "Player", RepathInterval = 0.1f });

        for (int i = 0; i < 30; i++)
            world.Update(1f / 60f);   // 0.5s ≈ 25px

        float first = enemy.Get<Transform>()!.Position.X;
        Assert.True(first > 10f, $"Não saiu do lugar: x={first}");

        // O alvo anda: sem repath, o inimigo pararia onde o jogador estava.
        playerTransform.Position = new Vector2(300f, 0f);

        for (int i = 0; i < 60; i++)
            world.Update(1f / 60f);

        Assert.True(enemy.Get<Transform>()!.Position.X > first + 20f,
            "Não reapontou depois que o alvo se moveu.");
    }

    [Fact]
    public void NavAgentParaDePerseguirForaDoFollowRange()
    {
        // Sem o alcance, a cena inteira de inimigos corre atrás do jogador de uma vez.
        var world = new World();
        world.CreateEntity("Player").Add(new Transform(new Vector2(1000f, 0f)));

        var enemy = world.CreateEntity("Slime");
        enemy.Add(new Transform());
        enemy.Add(new NavAgent { Speed = 50f, Follow = "Player", FollowRange = 100f });

        for (int i = 0; i < 60; i++)
            world.Update(1f / 60f);

        Assert.Equal(0f, enemy.Get<Transform>()!.Position.X, Tolerance);
    }

    [Fact]
    public void NavAgentComFollowSemAlvoNaCenaApenasPara()
    {
        var world = new World();
        var enemy = world.CreateEntity("Slime");
        enemy.Add(new Transform());
        var agent = new NavAgent { Follow = "NinguemComEsseNome" };
        enemy.Add(agent);

        world.Update(1f / 60f);

        Assert.False(agent.HasTarget);
    }

    // ---------- TopDownController ----------

    /// <summary>UIManager com uma tela contendo um joystick — é o caminho de input testável sem
    /// janela nem contexto de GL (teclado depende do IInputContext da plataforma).</summary>
    private UIManager LoadJoystickUi(string screen = "Hud", string joystick = "MoveStick")
    {
        Directory.CreateDirectory(_root);
        string path = $"{screen}.json";
        File.WriteAllText(Path.Combine(_root, path), $$"""
            {
              "Scene": "{{screen}}",
              "UI": true,
              "Objects": [
                { "Name": "{{joystick}}", "Components": [ { "Type": "UiJoystick", "Radius": 70 } ] }
              ]
            }
            """);

        var ui = new UIManager();
        ui.Load(path, new AssetManager(null!, new FileAssetSource(_root)));
        return ui;
    }

    [Fact]
    public void TopDownControllerAndaNaVelocidadeConfigurada()
    {
        var world = new World { UI = LoadJoystickUi() };
        world.UI!.Find<UiJoystick>("Hud", "MoveStick")!.Value = new Vector2(1f, 0f);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(new TopDownController
        {
            Speed = 100f,
            UseKeyboard = false,
            JoystickScreen = "Hud",
            JoystickName = "MoveStick",
        });

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);   // 1s

        Assert.Equal(100f, player.Get<Transform>()!.Position.X, 0.5f);
    }

    [Fact]
    public void DiagonalNaoEMaisRapidaQueAReta()
    {
        // O erro clássico do controlador top-down: sem normalizar, a diagonal anda raiz de 2
        // vezes mais rápido e o jogador aprende a andar sempre torto.
        var world = new World { UI = LoadJoystickUi() };
        world.UI!.Find<UiJoystick>("Hud", "MoveStick")!.Value = new Vector2(1f, 1f);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(new TopDownController
        {
            Speed = 100f,
            UseKeyboard = false,
            JoystickScreen = "Hud",
            JoystickName = "MoveStick",
        });

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);

        Assert.Equal(100f, player.Get<Transform>()!.Position.Length(), 0.5f);
    }

    [Fact]
    public void EmpurraoParcialDoJoystickAndaMaisDevagar()
    {
        // Magnitude menor que 1 tem que sobreviver: é o que dá passo lento no analógico.
        var world = new World { UI = LoadJoystickUi() };
        world.UI!.Find<UiJoystick>("Hud", "MoveStick")!.Value = new Vector2(0.5f, 0f);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        player.Add(new TopDownController
        {
            Speed = 100f,
            UseKeyboard = false,
            JoystickScreen = "Hud",
            JoystickName = "MoveStick",
        });

        for (int i = 0; i < 100; i++)
            world.Update(0.01f);

        Assert.Equal(50f, player.Get<Transform>()!.Position.X, 0.5f);
    }

    [Fact]
    public void AndarParaEsquerdaEspelhaOSpriteEAtualizaOFacing()
    {
        var world = new World { UI = LoadJoystickUi() };
        world.UI!.Find<UiJoystick>("Hud", "MoveStick")!.Value = new Vector2(-1f, 0f);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        var sprite = new SpriteRenderer();
        player.Add(sprite);
        var controller = new TopDownController
        {
            UseKeyboard = false,
            JoystickScreen = "Hud",
            JoystickName = "MoveStick",
        };
        player.Add(controller);

        world.Update(1f / 60f);

        Assert.True(sprite.FlipX);
        Assert.Equal(-1f, controller.Facing.X, Tolerance);
    }

    [Fact]
    public void ParadoNaoMexeNoFlipNemNoFacing()
    {
        // Soltar o controle não pode virar o personagem pra frente: a última direção é a pose em
        // que ele fica, e é por ela que o AttackSpawner mira.
        var world = new World { UI = LoadJoystickUi() };
        var stick = world.UI!.Find<UiJoystick>("Hud", "MoveStick")!;
        stick.Value = new Vector2(-1f, 0f);

        var player = world.CreateEntity("Player");
        player.Add(new Transform());
        var controller = new TopDownController
        {
            UseKeyboard = false,
            JoystickScreen = "Hud",
            JoystickName = "MoveStick",
        };
        player.Add(controller);

        world.Update(1f / 60f);
        stick.Value = Vector2.Zero;
        world.Update(1f / 60f);

        Assert.Equal(-1f, controller.Facing.X, Tolerance);
        Assert.Equal(0f, controller.Velocity.Length(), Tolerance);
    }
}
