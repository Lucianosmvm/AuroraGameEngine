using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Silk.NET.Maths;

namespace AuroraFarm;

/// <summary>
/// Base de um jogo de fazenda estilo Stardew Valley: fazenda com tilemap capinável,
/// plantio com crescimento por tempo, colheita por ouro, casa/celeiro/poço decorativos
/// e controle por toque (joystick + botão de ação) pronto para Android.
/// Todo o mundo é montado em código (<see cref="BuildWorld"/>) em vez de cena JSON —
/// mais fácil de continuar sem abrir o editor.
/// </summary>
public sealed class FarmGame : Game
{
    private readonly bool _smokeTest;
    private float _elapsed;
    private Font _font = null!;

    public const int MapWidth = 14;
    public const int MapHeight = 10;
    public const int TileSize = 64;

    public FarmGame(bool smokeTest = false)
    {
        _smokeTest = smokeTest;

        // Paisagem 1280x720 — trava câmera/UI/toque nessa proporção em qualquer aparelho.
        // Pra virar retrato depois: DesignResolution = new(720, 1280) e ajuste o Hud.json.
        DesignResolution = new Vector2D<int>(1280, 720);
    }

    protected override void OnLoad()
    {
        // Verde de fundo (em vez do azul padrão) pra área fora da fazenda parecer mais
        // campo/gramado do que céu, já que não há camada de horizonte.
        ClearColor = Color.FromBytes(78, 138, 58);

        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
        UI.Load("scenes/Hud.json", Assets);

        State.SetVariable("Gold", 0);

        BuildWorld();
    }

    protected override void OnUpdate(float deltaTime)
    {
        _elapsed += deltaTime;

        // --smoke: fecha sozinho depois de confirmar que carregou (usado em CI/teste automatizado).
        if (_smokeTest && _elapsed > 1.5f)
            Exit();
    }

    protected override void OnRenderUI(float dt)
    {
        // ScreenSize (não View.FramebufferSize): é o tamanho que UI.Update usa no hit-test
        // dos botões e que respeita o DesignResolution.
        UI.Draw(SpriteBatch, _font, State, Inventory, Quests, ScreenSize.X, ScreenSize.Y);
    }

    private void BuildWorld()
    {
        var mapOrigin = new Vector2(-(MapWidth * TileSize) / 2f, -(MapHeight * TileSize) / 2f);

        // --- Terreno: tileset com 4 tiles (grama, terra arada, caminho, água) recortado
        // do sprite sheet rtp.jpeg em docs/. Só grama e terra arada são usados agora —
        // caminho/água ficam prontos no tileset pra você desenhar bordas/lagoa depois.
        var terrain = Assets.LoadTexture("tilesets/farm_terrain.png");
        var farmland = World.CreateEntity("Farmland");
        farmland.Add(new Transform(mapOrigin));
        var tilemap = farmland.Add(new Tilemap
        {
            Tileset = terrain,
            TileWidth = TileSize,
            TileHeight = TileSize,
            Width = MapWidth,
            Height = MapHeight,
            Layer = 0,
        });
        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < MapWidth; x++)
                tilemap.SetTile(x, y, PlayerFarmer.GrassTile);

        // --- Cenário ao redor da lavoura (casa, celeiro, poço, árvores, cerca).
        SpawnDecor("sprites/farmhouse.png", new Vector2(-260, mapOrigin.Y - 90), solid: true, colliderSize: new Vector2(150, 70));
        SpawnDecor("sprites/barn.png", new Vector2(20, mapOrigin.Y - 90), solid: true, colliderSize: new Vector2(170, 70));
        SpawnDecor("sprites/well.png", new Vector2(300, mapOrigin.Y - 55), solid: true, colliderSize: new Vector2(80, 60));
        SpawnDecor("sprites/tree1.png", new Vector2(mapOrigin.X - 40, mapOrigin.Y + 70), solid: true, colliderSize: new Vector2(50, 50));
        SpawnDecor("sprites/tree2.png", new Vector2(mapOrigin.X + MapWidth * TileSize + 40, mapOrigin.Y + MapHeight * TileSize - 70), solid: true, colliderSize: new Vector2(50, 50));
        SpawnDecor("sprites/pine.png", new Vector2(mapOrigin.X - 40, mapOrigin.Y + MapHeight * TileSize - 70), solid: true, colliderSize: new Vector2(50, 50));

        for (int i = 0; i < 9; i++)
        {
            float x = mapOrigin.X + 20 + i * (MapWidth * TileSize - 40) / 8f;
            SpawnDecor("sprites/fence.png", new Vector2(x, mapOrigin.Y + MapHeight * TileSize + 26));
        }

        // --- Jogador.
        var player = World.CreateEntity("Player");
        player.Add(new Transform(Vector2.Zero));
        player.Add(new SpriteRenderer(Assets.LoadTexture("sprites/farmer.png"), layer: 10) { Size = new Vector2(46f, 46f) });
        player.Add(new Collider { Shape = ColliderShape.Box, Width = 26f, Height = 18f, Offset = new Vector2(0f, 14f) });
        player.Add(new PlayerFarmer
        {
            SeedlingTexture = Assets.LoadTexture("sprites/seedling.png"),
            GrownTexture = Assets.LoadTexture("sprites/crop_grown.png"),
        });

        // --- Câmera segue o jogador.
        var camera = World.CreateEntity("MainCamera");
        camera.Add(new Transform(Vector2.Zero));
        camera.Add(new CameraController { Follow = "Player", FollowSpeed = 6f, Zoom = 1f, ViewWidth = 1280, ViewHeight = 720 });
    }

    private void SpawnDecor(string texturePath, Vector2 position, bool solid = false, Vector2? colliderSize = null)
    {
        var texture = Assets.LoadTexture(texturePath);
        var entity = World.CreateEntity(System.IO.Path.GetFileNameWithoutExtension(texturePath));
        entity.Add(new Transform(position));
        entity.Add(new SpriteRenderer(texture, layer: 5));

        if (!solid)
            return;

        var size = colliderSize ?? new Vector2(texture.Width, texture.Height);
        entity.Add(new Collider
        {
            Shape = ColliderShape.Box,
            Width = size.X,
            Height = size.Y,
            // Empurra a caixa de colisão pra base do sprite (a arte tem o telhado acima do
            // centro) — sem isso o jogador esbarraria no ar em cima da casa/celeiro.
            Offset = new Vector2(0f, texture.Height / 2f - size.Y / 2f),
            IsSolid = true,
            IsKinematic = true,
        });
    }
}
