using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Silk.NET.Input;

namespace Aurora.Sandbox;

/// <summary>
/// Demo da Fase 1: ECS + SpriteBatch + câmera + input.
/// Desktop: WASD/setas movem, ESC sai. Android: toque segura e o jogador segue.
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

        var playerTexture = ProceduralTextures.Bordered(Gl, 32, 48,
            Color.FromBytes(223, 113, 38), Color.FromBytes(102, 57, 49));
        var treeTexture = ProceduralTextures.Bordered(Gl, 48, 64,
            Color.FromBytes(75, 105, 47), Color.FromBytes(52, 60, 28));
        var coinTexture = ProceduralTextures.Circle(Gl, 16, Color.FromBytes(251, 242, 54));

        _player = World.CreateEntity("Player");
        _player.Add(new Transform(0f, 0f));
        _player.Add(new SpriteRenderer(playerTexture, layer: 10));
        _player.Add(new PlayerController(Input, Camera));

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
