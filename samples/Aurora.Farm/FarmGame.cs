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

        // --- Lagoa de água animada, a leste da lavoura.
        BuildPond(new Vector2(mapOrigin.X + MapWidth * TileSize + 80f, -160f));

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

        // Ataque no clique do mouse: instancia o corte animado na direção do cursor.
        // Ver docs/TUTORIAL-SCRIPTS-PLAYER.md.
        player.Add(new PlayerAttack());

        // --- Câmera segue o jogador.
        var camera = World.CreateEntity("MainCamera");
        camera.Add(new Transform(Vector2.Zero));
        camera.Add(new CameraController { Follow = "Player", FollowSpeed = 6f, Zoom = 1f, ViewWidth = 1280, ViewHeight = 720 });
    }

    /// <summary>
    /// Lagoa de água animada: uma camada de <see cref="Tilemap"/> por cima do terreno, com o
    /// tileset gerado pelo <see cref="LiquidTileset"/> (16 colunas de máscara × 4 linhas de
    /// frame). O desenho da lagoa é só "marque as células molhadas"; quem escolhe margem,
    /// canto e miolo é o <c>Autotile()</c>, e quem faz a água mexer é o <c>AnimationFrames</c>.
    /// </summary>
    private void BuildPond(Vector2 origin)
    {
        // Metade do tile do terreno: a margem da água ganha o dobro de resolução sem precisar
        // redesenhar a lavoura inteira numa grade mais fina.
        const int Cell = TileSize / 2;
        const int Width = 12, Height = 12;

        var pond = World.CreateEntity("Lagoa");
        pond.Add(new Transform(origin));

        var water = pond.Add(new Tilemap
        {
            // Gerado por LiquidTileset.SavePng() — o mesmo PNG aparece na paleta do editor.
            // Pra gerar em runtime, sem asset: LiquidTileset.CreateTexture(Gl, LiquidStyle.Water()).
            Tileset = Assets.LoadTexture("tilesets/water.png"),
            TileWidth = Cell,
            TileHeight = Cell,
            Width = Width,
            Height = Height,
            Layer = 1,                     // acima do terreno (0), abaixo das árvores (5) e do jogador (10)
            AnimationFrames = 4,
            AnimationFrameDuration = 0.18f,
            AnimationColumns = LiquidTileset.Columns,
        });

        // Contorno oval amassado — pinte com qualquer índice >= 0, o Autotile reescreve tudo.
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float dx = (x - 5.5f) / 5.2f, dy = (y - 5.5f) / 5.0f;
                float r = dx * dx + dy * dy
                          + 0.12f * MathF.Sin(x * 1.9f) + 0.12f * MathF.Cos(y * 2.3f);
                if (r < 1f)
                    water.SetTile(x, y, 0);
            }
        }

        // outsideIsFilled: false — a lagoa acaba dentro do mapa, então a borda da grade é
        // margem de verdade (com true, um oceano que encosta na borda não ganharia espuma ali).
        water.Autotile(outsideIsFilled: false);

        // As 16 máscaras são todas água: o jogador contorna a lagoa em vez de atravessar.
        for (int mask = 0; mask <= LiquidTileset.Center; mask++)
            water.SolidTiles.Add(mask);
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
