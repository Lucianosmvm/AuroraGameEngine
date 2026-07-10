using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;
using Silk.NET.Input;

namespace Aurora.Sandbox;

/// <summary>
/// Demo das Fases 1/1.5/2-fundações: cena carregada de JSON, assets PNG reais,
/// ECS + SpriteBatch + câmera + input. Desktop: WASD/setas, ESC sai. Android: toque.
/// </summary>
public sealed class SandboxGame : Game
{
    private readonly bool _smokeTest;
    private float _elapsed;
    private float _titleTimer;

    private Entity _player;

    public SandboxGame(bool smokeTest = false)
    {
        _smokeTest = smokeTest;
    }

    protected override void OnLoad()
    {
        ClearColor = Color.FromBytes(34, 32, 52);

        // Behaviors do jogo entram no serializador pelo mesmo registro que os
        // componentes da engine — é o caminho que plugins usarão no futuro.
        Scenes.Register<PlayerController>("PlayerController",
            (_, _) => new PlayerController(Input, Camera),
            (_, _, _) => { });

        Scenes.Register<BobBehavior>("Bob",
            (json, _) => new BobBehavior(
                SceneSerializer.GetFloat(json, "Amplitude", 4f),
                SceneSerializer.GetFloat(json, "Frequency", 2f),
                SceneSerializer.GetFloat(json, "Phase", 0f)),
            (json, component, _) =>
            {
                var bob = (BobBehavior)component;
                json.WriteNumber("Amplitude", bob.Amplitude);
                json.WriteNumber("Frequency", bob.Frequency);
                json.WriteNumber("Phase", bob.Phase);
            });

        LoadScene("scenes/forest.json");

        if (!World.TryFind("Player", out _player))
            throw new InvalidOperationException("Cena forest.json não tem entidade 'Player'.");

        ScatterExtras();

        if (_smokeTest)
            VerifySceneRoundtrip();
    }

    /// <summary>Povoamento denso fica em código; a cena JSON guarda os objetos autorais.</summary>
    private void ScatterExtras()
    {
        var treeTexture = Assets.LoadTexture("sprites/tree.png");
        var coinTexture = Assets.LoadTexture("sprites/coin.png");
        var random = new Random(42);

        for (int i = 0; i < 40; i++)
        {
            var tree = World.CreateEntity($"Tree{i}");
            tree.Add(new Transform(random.Next(-1600, 1600), random.Next(-1200, 1200)));
            tree.Add(new SpriteRenderer(treeTexture, layer: 5));
        }

        for (int i = 0; i < 80; i++)
        {
            var coin = World.CreateEntity($"Coin{i}");
            coin.Add(new Transform(random.Next(-1600, 1600), random.Next(-1200, 1200)));
            coin.Add(new SpriteRenderer(coinTexture, layer: 6));
            coin.Add(new BobBehavior(amplitude: 4f, frequency: 3f, phase: i * 0.4f));
        }
    }

    /// <summary>Smoke test: salva o mundo inteiro e confere se o JSON tem todas as entidades.</summary>
    private void VerifySceneRoundtrip()
    {
        var context = new SceneContext { World = World, Assets = Assets };
        string json = Scenes.Save("Forest", context);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        int saved = document.RootElement.GetProperty("Objects").GetArrayLength();

        if (saved != World.EntityCount)
            throw new InvalidOperationException(
                $"Roundtrip de cena falhou: {World.EntityCount} entidades no mundo, {saved} no JSON.");

        Console.WriteLine($"[smoke] roundtrip de cena ok: {saved} entidades serializadas.");
    }

    protected override void OnUpdate(float deltaTime)
    {
        _elapsed += deltaTime;

        if (Input.IsKeyDown(Key.Escape) || (_smokeTest && _elapsed > 1.5f))
        {
            Exit();
            return;
        }

        Camera.Follow(_player.Get<Transform>()!.Position, speed: 6f, deltaTime);

        // Título só existe em janela desktop; view Android não tem título.
        if (Window is { } window)
        {
            _titleTimer += deltaTime;
            if (_titleTimer >= 0.25f)
            {
                _titleTimer = 0f;
                int fps = deltaTime > 0f ? (int)MathF.Round(1f / deltaTime) : 0;
                window.Title = $"Aurora Sandbox — {fps} FPS | {World.EntityCount} entidades | " +
                               $"{SpriteBatch.DrawCallsLastFrame} draw calls";
            }
        }
    }
}
