using System.Numerics;
using Aurora.Runtime;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.SlandSurvivor.Gameplay;
using Aurora.SlandSurvivor.Items;
using Aurora.SlandSurvivor.Saves;
using Aurora.SlandSurvivor.UI;
using Aurora.SlandSurvivor.Worlds;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace Aurora.SlandSurvivor;

/// <summary>
/// Sandbox 2D de mundo procedural: cave, construa, sobreviva à noite.
///
/// <para>O mundo inteiro (1200x300 tiles) vive em dois <see cref="Tilemap"/> — frente sólida
/// e parede de fundo — desenhados pela engine com recorte pela câmera, então o custo por
/// frame depende da tela e não do tamanho do mundo. Esta classe cuida do que não é
/// comportamento de entidade: relógio dia/noite, iluminação, nascimento de inimigos,
/// partículas, save e HUD.</para>
///
/// <para>Nada de arte em disco: tileset e personagens são pintados em código
/// (<see cref="TileDb.BuildAtlas"/>, <see cref="PixelArt"/>). O único asset é a fonte.</para>
/// </summary>
public sealed class SurvivorGame : Game
{
    // Um dia completo em segundos reais (24 h de jogo).
    private const float DayLength = 840f;
    private const float SeaLevelTile = 74f;

    private readonly int _seed;
    private readonly bool _smokeTest;
    private readonly LightMap _light = new();
    private readonly EnemySpawner _spawner = new();
    private readonly List<Particle> _particles = [];
    private readonly List<(Entity Entity, Transform Transform, EnemyBehavior Enemy)> _enemyBuffer = [];

    private Font _font = null!;
    private Font _small = null!;
    private Texture2D _tileset = null!;
    private Texture2D _playerTexture = null!;
    private Texture2D _slimeTexture = null!;
    private Texture2D _batTexture = null!;
    private Texture2D _zombieTexture = null!;
    private Texture2D _skyTexture = null!;
    private Hud _hud = null!;

    private Entity _player;
    private Transform? _skyTransform;
    private SpriteRenderer? _skySprite;
    private float _clock = 8f;                 // hora do jogo, 0–24
    private float _respawnTimer;
    private float _elapsed;
    private float _titleTimer;
    private int _smokeStep;
    private int _frames;
    private string? _pendingShot;
    private bool _screenshotTaken;
    private bool _exitAfterShot;

    public SurvivorGame(int seed, bool smokeTest = false)
    {
        _seed = seed;
        _smokeTest = smokeTest;
        GameName = "SlandSurvivor";
    }

    // ---------------------------------------------------------------------
    //  Estado exposto ao HUD e aos scripts
    // ---------------------------------------------------------------------

    /// <summary>Hora inicial do relógio (padrão 8 h). Útil para abrir o jogo à noite.</summary>
    public float StartClock { get; set; } = 8f;

    /// <summary>Nasce esta quantidade de tiles abaixo da superfície (0 = na grama).</summary>
    public int StartDepth { get; set; }

    /// <summary>Zoom da câmera (2 = padrão; maior aproxima).</summary>
    public float Zoom { get; set; } = 2f;

    /// <summary>Se definido, salva um PNG da tela depois de <see cref="ScreenshotDelay"/> e sai.</summary>
    public string? ScreenshotPath { get; set; }

    public float ScreenshotDelay { get; set; } = 1.5f;

    public TileWorld Tiles { get; private set; } = null!;
    public Inventory Backpack { get; } = new();
    public PlayerController? Player { get; private set; }
    public Transform? PlayerTransform { get; private set; }
    public int Day { get; private set; } = 1;
    public int EnemyCount { get; private set; }
    public bool PlayerIsDead => PlayerHealth <= 0f;

    public Vector2 DesignSize => new(ScreenSize.X, ScreenSize.Y);

    public float PlayerHealth => _player.IsAlive ? _player.Get<Health>()?.Current ?? 0f : 0f;
    public float PlayerMaxHealth => _player.IsAlive ? _player.Get<Health>()?.Max ?? 100f : 100f;
    public float PlayerHealthRatio => PlayerMaxHealth > 0f ? PlayerHealth / PlayerMaxHealth : 0f;

    public bool UiBlocksWorldClicks => _hud is not null && _hud.BlocksWorldClicks;

    public bool IsDaytime => _clock is >= 6f and < 19f;

    /// <summary>Intensidade da luz do céu: 1 ao meio-dia, quase 0 na madrugada.</summary>
    public float SkyFactor => _clock switch
    {
        < 4.5f or >= 20.5f => 0.10f,
        < 7f => Noise.Lerp(0.10f, 1f, (_clock - 4.5f) / 2.5f),
        < 18f => 1f,
        _ => Noise.Lerp(1f, 0.10f, (_clock - 18f) / 2.5f),
    };

    public string ClockText => $"{(int)_clock:00}:{(int)((_clock - (int)_clock) * 60f):00}";

    public string BiomeName
    {
        get
        {
            if (PlayerTransform is not { } transform)
                return "";

            int column = Math.Clamp(TileWorld.ToTile(transform.Position.X), 0, Tiles.Width - 1);
            return Tiles.Biomes[column] switch
            {
                Biome.Desert => "Deserto",
                Biome.Snow => "Tundra",
                _ => "Floresta",
            };
        }
    }

    public string DepthText
    {
        get
        {
            if (PlayerTransform is not { } transform)
                return "";

            int meters = (int)((TileWorld.ToTile(transform.Position.Y) - SeaLevelTile) * 2f);
            return meters >= 0 ? $"Profundidade: {meters} m" : $"Altitude: {-meters} m";
        }
    }

    // ---------------------------------------------------------------------
    //  Carregamento
    // ---------------------------------------------------------------------

    protected override void OnLoad()
    {
        DesignResolution = new Vector2D<int>(1280, 720);

        // Teto de dt menor que o padrão: a colisão testa sobreposição na posição já atualizada,
        // então um frame longo faria o jogador atravessar um tile de 16 px. 1/45 s com queda
        // máxima de 620 px/s dá ~14 px por passo.
        MaxDeltaTime = 1f / 45f;

        _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
        _small = Assets.LoadFont("fonts/DejaVuSans.ttf", 17f);

        _tileset = Texture2D.FromPixels(Gl, TileDb.AtlasWidth, TileDb.AtlasHeight, TileDb.BuildAtlas());
        _playerTexture = PixelArt.Player(Gl);
        _slimeTexture = PixelArt.Slime(Gl);
        _batTexture = PixelArt.Bat(Gl);
        _zombieTexture = PixelArt.Zombie(Gl);
        _skyTexture = BuildDisc(96);

        _clock = Math.Clamp(StartClock, 0f, 23.99f);

        Console.WriteLine($"[mundo] gerando com seed {_seed}...");
        var generated = WorldGen.Generate(_seed);
        BuildWorld(generated);

        if (StartDepth > 0)
            DigStartingShaft(generated.SpawnX, generated.SpawnY);

        _hud = new Hud(Gl, _font, _small, _tileset, this);
        Backpack.Add(ItemIds.Torch, 10);

        Console.WriteLine($"[mundo] pronto: {generated.Width}x{generated.Height} tiles, " +
                          $"nascimento em ({generated.SpawnX}, {generated.SpawnY}).");
    }

    /// <summary>Monta (ou remonta) o mundo e todas as entidades a partir de uma geração.</summary>
    private void BuildWorld(GeneratedWorld generated)
    {
        World.Clear();
        _particles.Clear();

        var background = World.CreateEntity("Paredes");
        background.Add(new Transform(Vector2.Zero));
        var backgroundMap = background.Add(new Tilemap { Layer = -20, Tileset = _tileset });

        var foreground = World.CreateEntity("Mundo");
        foreground.Add(new Transform(Vector2.Zero));
        var foregroundMap = foreground.Add(new Tilemap { Layer = 0, Tileset = _tileset });

        Tiles = new TileWorld(generated, foregroundMap, backgroundMap);

        // Céu: sol/lua desenhados atrás de tudo, presos à câmera (parallax zero).
        var sky = World.CreateEntity("Sol");
        _skyTransform = sky.Add(new Transform(Vector2.Zero));
        _skySprite = sky.Add(new SpriteRenderer(_skyTexture, -200) { Size = new Vector2(150f, 150f) });

        SpawnPlayer(TileWorld.TileCenter(generated.SpawnX, generated.SpawnY));

        var camera = World.CreateEntity("Câmera");
        camera.Add(new Transform(PlayerTransform?.Position ?? Vector2.Zero));
        camera.Add(new CameraController
        {
            Follow = "Player",
            FollowSpeed = 9f,
            Zoom = Zoom,
            ClampBounds = true,
            BoundsX = 0f,
            BoundsY = 0f,
            BoundsWidth = Tiles.WorldWidth,
            BoundsHeight = Tiles.WorldHeight,
        });
    }

    /// <summary>
    /// Abre um poço de 2 tiles do chão até <see cref="StartDepth"/> e larga o jogador lá
    /// embaixo. Só existe para abrir o jogo direto na caverna (depuração e capturas).
    /// </summary>
    private void DigStartingShaft(int column, int surfaceY)
    {
        int bottom = Math.Min(Tiles.Height - 8, surfaceY + StartDepth);

        for (int y = surfaceY; y <= bottom; y++)
        {
            Tiles.SetTile(column, y, TileId.Empty);
            Tiles.SetTile(column + 1, y, TileId.Empty);

            // Tocha na parede a cada 7 tiles — descer um poço às escuras não mostra nada.
            if (y % 7 == 0 && Tiles.IsSolid(column - 1, y))
                Tiles.SetTile(column, y, TileId.Torch);
        }

        Player?.Teleport(TileWorld.TileCenter(column, bottom - 1));
    }

    private void SpawnPlayer(Vector2 position)
    {
        var entity = World.CreateEntity("Player");
        PlayerTransform = entity.Add(new Transform(position));
        entity.Add(new SpriteRenderer(_playerTexture, 10)
        {
            Size = new Vector2(PlayerController.HalfWidth * 2f, PlayerController.HalfHeight * 2f),
        });
        entity.Add(new Collider
        {
            Width = PlayerController.HalfWidth * 2f,
            Height = PlayerController.HalfHeight * 2f,
            Layer = 1,
            Mask = 0,                                     // só colide com o tilemap
        });
        entity.Add(new Health { Max = 100f, Current = 100f, InvulnerabilityAfterHit = 0.7f, DestroyOnDeath = false });

        Player = entity.Add(new PlayerController
        {
            Tiles = Tiles,
            Game = this,
            Backpack = Backpack,
            Cam = Camera,
        });

        _player = entity;
    }

    // ---------------------------------------------------------------------
    //  Loop
    // ---------------------------------------------------------------------

    protected override void OnUpdate(float deltaTime)
    {
        _elapsed += deltaTime;
        _frames++;

        if (Input.IsKeyDown(Key.Escape))
        {
            Exit();
            return;
        }

        AdvanceClock(deltaTime);
        HandleHotkeys();
        UpdateSky();
        UpdateParticles(deltaTime);

        EnemyCount = World.Query<EnemyBehavior>().Count();
        _spawner.Update(this, Tiles, deltaTime);

        _hud.Update(Input, Backpack, deltaTime);

        if (PlayerIsDead)
            UpdateRespawn(deltaTime);

        if (_smokeTest)
            RunSmoke();

        UpdateWindowTitle(deltaTime);
    }

    private void AdvanceClock(float deltaTime)
    {
        _clock += deltaTime * (24f / DayLength);

        if (_clock >= 24f)
        {
            _clock -= 24f;
            Day++;
            Notify($"Amanheceu - dia {Day}.");
        }

        // Cor do céu: noite → dia, com um empurrão alaranjado no nascer e no pôr do sol.
        var night = new Vector3(0.05f, 0.06f, 0.13f);
        var day = new Vector3(0.38f, 0.58f, 0.84f);
        var dusk = new Vector3(0.86f, 0.47f, 0.28f);

        var color = Vector3.Lerp(night, day, SkyFactor);
        float duskWeight = MathF.Max(Bell(_clock, 6f, 1.6f), Bell(_clock, 19f, 1.6f));
        color = Vector3.Lerp(color, dusk, duskWeight * 0.65f);

        ClearColor = new Color(color.X, color.Y, color.Z);
    }

    private static float Bell(float value, float center, float width)
        => MathF.Max(0f, 1f - MathF.Abs(value - center) / width);

    private void HandleHotkeys()
    {
        for (int i = 0; i < 10; i++)
        {
            // Key.Number1..Number9 são sequenciais; o 0 fica no fim da barra (espaço 10).
            var key = i < 9 ? Key.Number1 + i : Key.Number0;
            if (Input.WasKeyPressed(key))
                Backpack.Selected = i;
        }

        if (Input.WasKeyPressed(Key.Q))
            Backpack.Selected--;
        if (Input.WasKeyPressed(Key.F5))
            SaveWorld();
        if (Input.WasKeyPressed(Key.F9))
            LoadWorld();
        if (Input.WasKeyPressed(Key.F12))
            _pendingShot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "SlandSurvivor", $"print-{DateTime.Now:yyyyMMdd-HHmmss}.png");

        if (ScreenshotPath is { } automatic && _elapsed >= ScreenshotDelay && !_screenshotTaken)
        {
            _pendingShot = automatic;
            _screenshotTaken = true;
            _exitAfterShot = true;
        }
    }

    /// <summary>
    /// Grava o framebuffer em PNG. Precisa fechar o lote atual antes de ler os pixels — o
    /// SpriteBatch ainda tem quads na fila neste ponto do frame, e a GPU só desenhou o que
    /// já foi enviado. Reabre em seguida para o <c>End()</c> do loop encontrar o lote válido.
    /// </summary>
    private void CaptureScreenshot(string path)
    {
        SpriteBatch.End();
        Gl.Finish();

        var size = View.FramebufferSize;
        var pixels = new byte[size.X * size.Y * 4];

        Gl.PixelStore(Silk.NET.OpenGL.PixelStoreParameter.PackAlignment, 1);
        Gl.ReadPixels<byte>(0, 0, (uint)size.X, (uint)size.Y,
            Silk.NET.OpenGL.GLEnum.Rgba, Silk.NET.OpenGL.GLEnum.UnsignedByte, out pixels[0]);

        // OpenGL entrega a imagem de baixo para cima; PNG espera de cima para baixo.
        Tools.PngWriter.Write(path, size.X, size.Y, pixels, flipVertically: true);
        int averageFps = _elapsed > 0f ? (int)MathF.Round(_frames / _elapsed) : 0;
        Console.WriteLine($"[print] {path} ({averageFps} FPS médios)");
        Notify("Captura salva.");

        SpriteBatch.Begin(GetScreenProjection());
    }

    private void UpdateSky()
    {
        if (_skyTransform is null || _skySprite is null)
            return;

        // Arco do sol/lua sobre a câmera: 6h à esquerda, 18h à direita.
        float phase = (_clock - 6f) / 12f;                  // 0..1 durante o dia
        bool isNight = phase is < 0f or > 1f;
        if (isNight)
            phase = _clock < 6f ? (_clock + 6f) / 12f : (_clock - 18f) / 12f;

        float angle = MathF.PI * Math.Clamp(phase, 0f, 1f);
        _skyTransform.Position = Camera.Position
            + new Vector2(-MathF.Cos(angle) * 240f, -MathF.Sin(angle) * 120f - 60f);

        _skySprite.Color = isNight
            ? Color.FromBytes(226, 232, 244, 210)           // lua
            : Color.FromBytes(255, 238, 176, 200);          // sol
        _skySprite.Size = new Vector2(isNight ? 78f : 116f, isNight ? 78f : 116f);
    }

    private void UpdateRespawn(float deltaTime)
    {
        _respawnTimer -= deltaTime;
        if (_respawnTimer > 0f || Player is null)
            return;

        Player.Teleport(TileWorld.TileCenter((int)Tiles.SpawnTile.X, (int)Tiles.SpawnTile.Y));

        if (_player.Get<Health>() is { } health)
            health.Current = health.Max;

        Notify("De volta ao ponto de nascimento.");
    }

    private void UpdateWindowTitle(float deltaTime)
    {
        if (Window is not { } window)
            return;

        _titleTimer += deltaTime;
        if (_titleTimer < 0.25f)
            return;

        _titleTimer = 0f;
        int fps = deltaTime > 0f ? (int)MathF.Round(1f / deltaTime) : 0;
        window.Title = $"Sland Survivor — {fps} FPS | seed {Tiles.Seed} | dia {Day} {ClockText} | " +
                       $"{World.EntityCount} entidades | {SpriteBatch.DrawCallsLastFrame} draw calls";
    }

    // ---------------------------------------------------------------------
    //  Desenho do mundo (passe com a câmera)
    // ---------------------------------------------------------------------

    protected override void OnRender(float deltaTime)
    {
        var (min, max) = Camera.GetVisibleBounds();
        int x0 = TileWorld.ToTile(min.X) - 12;
        int y0 = TileWorld.ToTile(min.Y) - 12;
        int width = TileWorld.ToTile(max.X) - x0 + 14;
        int height = TileWorld.ToTile(max.Y) - y0 + 14;

        // Brilho fraco que o jogador carrega: alcança uns 4 tiles no ar, o suficiente para
        // não descer completamente às cegas e longe de tornar a tocha dispensável.
        _light.ExtraSources.Clear();
        if (PlayerTransform is { } lightSource)
        {
            _light.ExtraSources.Add((TileWorld.ToTile(lightSource.Position.X),
                TileWorld.ToTile(lightSource.Position.Y), 7));
        }

        _light.Compute(Tiles, x0, y0, width, height, SkyFactor);

        DrawItemDrops();
        DrawHeldItem();
        DrawParticles();
        DrawDarkness(min, max);
        DrawGlows(min, max);
        DrawMiningCursor();
    }

    private void DrawItemDrops()
    {
        foreach (var (_, transform, drop) in World.Query<Transform, ItemDropBehavior>())
        {
            var position = transform.Position + new Vector2(-ItemDropBehavior.Size * 0.5f,
                -ItemDropBehavior.Size * 0.5f + drop.Bob);
            ItemDb.DrawIcon(SpriteBatch, _tileset, drop.Item, position, ItemDropBehavior.Size);
        }
    }

    /// <summary>Item na mão do jogador, avançando num arco enquanto o golpe acontece.</summary>
    private void DrawHeldItem()
    {
        if (Player is null || PlayerTransform is null || Backpack.SelectedItem < 0)
            return;

        float swing = Player.SwingProgress;
        var direction = Player.SwingDirection;
        if (swing <= 0f)
            direction = new Vector2(Player.FacingLeft ? -1f : 1f, -0.25f);

        float reach = 9f + MathF.Sin(swing * MathF.PI) * 12f;
        var position = PlayerTransform.Position + Vector2.Normalize(direction) * reach - new Vector2(5f, 5f);
        ItemDb.DrawIcon(SpriteBatch, _tileset, Backpack.SelectedItem, position, 10f);
    }

    /// <summary>
    /// Escuridão: um quad preto por tile visível, com alpha vindo do <see cref="LightMap"/>.
    /// Desenhado depois de tudo do mundo, então também escurece jogador, bichos e itens —
    /// é o que faz a tocha valer alguma coisa lá embaixo.
    /// </summary>
    private void DrawDarkness(Vector2 min, Vector2 max)
    {
        int firstX = TileWorld.ToTile(min.X) - 1;
        int firstY = TileWorld.ToTile(min.Y) - 1;
        int lastX = TileWorld.ToTile(max.X) + 1;
        int lastY = TileWorld.ToTile(max.Y) + 1;

        // Cada tile vira 4 quadrantes com alpha interpolado entre os cantos: o degradê fica
        // suave sem shader e sem mapa de luz em resolução maior. Os quadrantes passam meio
        // pixel do tamanho para se sobrepor — melhor que deixar uma linha clara de 1 px
        // aparecendo entre eles no meio de uma caverna escura.
        const float quarter = TileWorld.TileSize * 0.5f;
        var quadSize = new Vector2(quarter + 0.6f, quarter + 0.6f);

        for (int y = firstY; y <= lastY; y++)
        {
            for (int x = firstX; x <= lastX; x++)
            {
                float c00 = _light.Corner(x, y);
                float c10 = _light.Corner(x + 1, y);
                float c01 = _light.Corner(x, y + 1);
                float c11 = _light.Corner(x + 1, y + 1);

                if (c00 > 0.98f && c10 > 0.98f && c01 > 0.98f && c11 > 0.98f)
                    continue;                              // céu aberto: nada a escurecer

                var origin = TileWorld.TileToWorld(x, y);

                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    float u = (quadrant & 1) == 0 ? 0.25f : 0.75f;
                    float v = quadrant < 2 ? 0.25f : 0.75f;

                    float light = Noise.Lerp(Noise.Lerp(c00, c10, u), Noise.Lerp(c01, c11, u), v);
                    float alpha = 1f - light;
                    if (alpha <= 0.02f)
                        continue;

                    SpriteBatch.DrawRect(
                        origin + new Vector2(u < 0.5f ? 0f : quarter, v < 0.5f ? 0f : quarter),
                        quadSize, new Color(0.015f, 0.015f, 0.03f, MathF.Min(alpha, 0.955f)));
                }
            }
        }
    }

    private void DrawGlows(Vector2 min, Vector2 max)
    {
        int firstX = TileWorld.ToTile(min.X);
        int firstY = TileWorld.ToTile(min.Y);
        int lastX = TileWorld.ToTile(max.X);
        int lastY = TileWorld.ToTile(max.Y);

        for (int y = firstY; y <= lastY; y++)
        {
            for (int x = firstX; x <= lastX; x++)
            {
                int tile = Tiles.Get(x, y);
                if (tile != TileId.Torch)
                    continue;

                // Chama tremeluzindo: o raio oscila com o tempo e com a posição do tile,
                // senão todas as tochas piscariam juntas.
                float flicker = 0.9f + MathF.Sin(_elapsed * 7f + x * 1.7f + y * 0.9f) * 0.08f;
                SpriteBatch.DrawGlow(TileWorld.TileCenter(x, y), 46f * flicker,
                    Color.FromBytes(255, 176, 84, 82));
            }
        }
    }

    private void DrawMiningCursor()
    {
        if (Player?.AimTile is not { } aim)
            return;

        var position = TileWorld.TileToWorld(aim.X, aim.Y);
        var size = new Vector2(TileWorld.TileSize, TileWorld.TileSize);

        if (TileDb.Get(Tiles.Get(aim.X, aim.Y)) is { Hardness: > 0f } def)
        {
            float progress = Math.Clamp(Tiles.DamageAt(aim.X, aim.Y) / def.Hardness, 0f, 1f);
            if (progress > 0f)
                SpriteBatch.DrawRect(position, size, new Color(0f, 0f, 0f, progress * 0.55f));
        }

        var outline = new Color(1f, 1f, 1f, 0.55f);
        SpriteBatch.DrawRect(position, new Vector2(size.X, 1f), outline);
        SpriteBatch.DrawRect(position + new Vector2(0f, size.Y - 1f), new Vector2(size.X, 1f), outline);
        SpriteBatch.DrawRect(position, new Vector2(1f, size.Y), outline);
        SpriteBatch.DrawRect(position + new Vector2(size.X - 1f, 0f), new Vector2(1f, size.Y), outline);
    }

    protected override void OnRenderUI(float deltaTime)
    {
        _hud.Draw(SpriteBatch, Backpack, DesignSize);

        if (_pendingShot is not { } path)
            return;

        _pendingShot = null;
        CaptureScreenshot(path);

        if (_exitAfterShot)
            Exit();
    }

    protected override void OnUnload()
    {
        _hud?.Dispose();
        _tileset?.Dispose();
        _playerTexture?.Dispose();
        _slimeTexture?.Dispose();
        _batTexture?.Dispose();
        _zombieTexture?.Dispose();
        _skyTexture?.Dispose();
    }

    // ---------------------------------------------------------------------
    //  Serviços usados pelos scripts
    // ---------------------------------------------------------------------

    public void Notify(string message) => _hud?.Notify(message);

    /// <summary>Guarda o item; devolve false se a mochila estiver cheia (o item fica no chão).</summary>
    public bool Collect(int item, int count)
    {
        int leftover = Backpack.Add(item, count);
        if (leftover > 0)
        {
            Notify("Mochila cheia.");
            return false;
        }

        return true;
    }

    public void SpawnDrop(Vector2 position, int item, int count)
    {
        if (item < 0 || count <= 0)
            return;

        var entity = World.CreateEntity($"Item:{ItemDb.NameOf(item)}");
        entity.Add(new Transform(position));
        entity.Add(new Collider
        {
            Width = ItemDropBehavior.Size,
            Height = ItemDropBehavior.Size,
            Layer = 4,
            Mask = 0,
        });

        var drop = entity.Add(new ItemDropBehavior { Item = item, Count = count, Tiles = Tiles, Game = this });

        // Um empurrãozinho aleatório para dois itens do mesmo bloco não ficarem sobrepostos.
        float spread = Noise.Hash(entity.Id, (int)position.X, (int)position.Y) * 2f - 1f;
        drop.Launch(new Vector2(spread * 40f, -70f));
    }

    public void SpawnEnemy(EnemyKind kind, Vector2 position)
    {
        var entity = World.CreateEntity(kind.ToString());
        entity.Add(new Transform(position));

        var (texture, size, behavior, health) = kind switch
        {
            EnemyKind.Slime => (_slimeTexture, new Vector2(20f, 15f),
                new EnemyBehavior { Kind = kind, Speed = 62f, ContactDamage = 12f, JumpSpeed = 300f,
                    LootItem = ItemIds.Gel, LootMin = 1, LootMax = 3 }, 40f),

            EnemyKind.Zombie => (_zombieTexture, new Vector2(13f, 23f),
                new EnemyBehavior { Kind = kind, Speed = 46f, ContactDamage = 18f, JumpSpeed = 330f,
                    LootItem = ItemIds.Gel, LootMin = 0, LootMax = 2 }, 70f),

            _ => (_batTexture, new Vector2(20f, 13f),
                new EnemyBehavior { Kind = kind, Speed = 96f, ContactDamage = 10f, Flying = true,
                    LootItem = ItemIds.Gel, LootMin = 0, LootMax = 1 }, 32f),
        };

        entity.Add(new SpriteRenderer(texture, 8) { Size = size });
        entity.Add(new Collider { Width = size.X - 3f, Height = size.Y - 2f, Layer = 2, Mask = 0 });
        entity.Add(new Health { Max = health, Current = health, InvulnerabilityAfterHit = 0.12f });

        behavior.Tiles = Tiles;
        behavior.Game = this;
        entity.Add(behavior);
    }

    /// <summary>Golpe do jogador: acerta todo inimigo dentro do raio, com empurrão.</summary>
    public void HitEnemies(Vector2 center, float radius, float damage, Vector2 direction, Entity source)
    {
        // Snapshot antes de bater: matar um inimigo cria itens no chão, e criar entidade no
        // meio de um Query (que enumera dicionário) explodiria a iteração.
        _enemyBuffer.Clear();
        foreach (var entry in World.Query<Transform, EnemyBehavior>())
            _enemyBuffer.Add(entry);

        bool hitSomething = false;

        foreach (var (entity, transform, enemy) in _enemyBuffer)
        {
            if (Vector2.Distance(transform.Position, center) > radius)
                continue;

            if (!World.Damage(entity, damage, source))
                continue;

            enemy.Knockback(direction, 190f);
            hitSomething = true;
        }

        if (hitSomething)
            SpawnHitEffect(center);
    }

    public void DamagePlayer(float amount, Vector2 direction, float force, Entity source)
    {
        if (!_player.IsAlive || Player is null)
            return;

        if (!World.Damage(_player, amount, source))
            return;                                        // ainda invencível do golpe anterior

        if (float.IsFinite(direction.X) && float.IsFinite(direction.Y))
            Player.Knockback(direction, force);
    }

    public void OnPlayerHurt(float amount)
    {
        if (PlayerTransform is { } transform)
            SpawnParticles(transform.Position, Color.FromBytes(220, 60, 70), 10, 90f, 2.5f);
    }

    public void OnPlayerDeath()
    {
        _respawnTimer = 2.2f;
        Notify("Você morreu.");

        if (PlayerTransform is { } transform)
            SpawnParticles(transform.Position, Color.FromBytes(200, 40, 50), 26, 140f, 3f);
    }

    public void OnTileBroken(int x, int y, int tile, int drop)
    {
        var center = TileWorld.TileCenter(x, y);

        if (TileDb.Get(tile) is { } def)
            SpawnParticles(center, ItemDb.ToColor(def.Base), 8, 70f, 2.5f);

        if (drop >= 0)
            SpawnDrop(center, drop, 1);

        // Bloco de baixo perde a grama quando fica exposto ao sol? Não: aqui só derrubamos o
        // que estava apoiado (folha e cacto ficam boiando se o tronco sumir).
        DropUnsupported(x, y - 1);
    }

    public void OnTilePlaced(int x, int y, int tile)
        => SpawnParticles(TileWorld.TileCenter(x, y),
            ItemDb.ToColor(TileDb.Get(tile)?.Base ?? new Rgb(200, 200, 200)), 5, 45f, 2f);

    /// <summary>Folhas/cactos que ficaram sem apoio viram item — evita árvore flutuante.</summary>
    private void DropUnsupported(int x, int y)
    {
        int tile = Tiles.Get(x, y);
        if (tile is not (TileId.Leaves or TileId.Cactus or TileId.Torch))
            return;

        bool supported = Tiles.IsSolid(x, y + 1) || Tiles.IsSolid(x - 1, y) || Tiles.IsSolid(x + 1, y)
            || Tiles.IsSolid(x, y - 1);

        if (supported)
            return;

        int drop = TileDb.Get(tile)?.Drop ?? ItemIds.None;
        Tiles.SetTile(x, y, TileId.Empty);

        if (drop >= 0)
            SpawnDrop(TileWorld.TileCenter(x, y), drop, 1);
    }

    public void SpawnHitEffect(Vector2 position)
        => SpawnParticles(position, Color.FromBytes(255, 236, 180), 8, 120f, 2f);

    // ---------------------------------------------------------------------
    //  Partículas
    // ---------------------------------------------------------------------

    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float MaxLife;
        public float Size;
        public Color Color;
    }

    private void SpawnParticles(Vector2 position, Color color, int count, float speed, float size)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = Noise.Hash(i * 31 + (int)position.X, (int)position.Y, _particles.Count) * MathF.Tau;
            float velocity = speed * (0.35f + Noise.Hash(i, (int)position.Y, _particles.Count + 7) * 0.8f);

            _particles.Add(new Particle
            {
                Position = position,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * velocity,
                Life = 0.45f + Noise.Hash(i, 5, _particles.Count) * 0.35f,
                MaxLife = 0.8f,
                Size = size,
                Color = color,
            });
        }
    }

    private void UpdateParticles(float deltaTime)
    {
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var particle = _particles[i];
            particle.Life -= deltaTime;

            if (particle.Life <= 0f)
            {
                _particles.RemoveAt(i);
                continue;
            }

            particle.Velocity.Y += 620f * deltaTime;
            particle.Position += particle.Velocity * deltaTime;
            _particles[i] = particle;
        }
    }

    private void DrawParticles()
    {
        foreach (var particle in _particles)
        {
            float fade = Math.Clamp(particle.Life / particle.MaxLife, 0f, 1f);
            SpriteBatch.DrawRect(particle.Position, new Vector2(particle.Size, particle.Size),
                particle.Color.WithAlpha(fade));
        }
    }

    // ---------------------------------------------------------------------
    //  Save
    // ---------------------------------------------------------------------

    public void SaveWorld(string? path = null)
    {
        var data = WorldSave.FromInventory(Backpack);
        data.Seed = Tiles.Seed;
        data.Width = Tiles.Width;
        data.Height = Tiles.Height;
        data.Foreground = Tiles.Foreground.Tiles;
        data.Background = Tiles.Background.Tiles;
        data.PlayerX = PlayerTransform?.Position.X ?? 0f;
        data.PlayerY = PlayerTransform?.Position.Y ?? 0f;
        data.Health = PlayerHealth;
        data.Clock = _clock;
        data.Day = Day;

        WorldSave.Save(data, path);
        Notify("Mundo salvo.");
    }

    public bool LoadWorld(string? path = null)
    {
        var data = WorldSave.Load(path);
        if (data is null)
        {
            Notify("Nenhum save encontrado.");
            return false;
        }

        // O save guarda os tiles, não os metadados derivados (bioma, relevo): regerar pela
        // seed sai mais barato que gravá-los, e é sempre consistente com a versão do gerador.
        var generated = WorldGen.Generate(data.Seed, data.Width, data.Height);
        Array.Copy(data.Foreground, generated.Foreground, data.Foreground.Length);
        Array.Copy(data.Background, generated.Background, data.Background.Length);

        BuildWorld(generated);
        Tiles.RebuildSkyTop();

        WorldSave.ApplyInventory(data, Backpack);
        _clock = data.Clock;
        Day = data.Day;

        Player?.Teleport(new Vector2(data.PlayerX, data.PlayerY));
        if (_player.Get<Health>() is { } health)
            health.Current = Math.Clamp(data.Health, 1f, health.Max);

        Notify("Mundo carregado.");
        return true;
    }

    // ---------------------------------------------------------------------
    //  Utilidades
    // ---------------------------------------------------------------------

    /// <summary>Disco branco com borda suave — sol, lua e qualquer brilho redondo.</summary>
    private Texture2D BuildDisc(int size)
    {
        var pixels = new byte[size * size * 4];
        float center = (size - 1) / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center, dy = y - center;
                float distance = MathF.Sqrt(dx * dx + dy * dy) / center;
                float alpha = distance < 0.55f ? 1f : MathF.Pow(Math.Clamp(1f - (distance - 0.55f) / 0.45f, 0f, 1f), 2.2f);

                int i = (y * size + x) * 4;
                pixels[i + 0] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = (byte)(alpha * 255f);
            }
        }

        return Texture2D.FromPixels(Gl, size, size, pixels);
    }

    // ---------------------------------------------------------------------
    //  Roteiro do --smoke
    // ---------------------------------------------------------------------

    /// <summary>
    /// Verificação automatizada (CI): confere que o mundo gerado tem terreno, cavernas,
    /// minério e rocha-mãe; que o jogador pousa no chão; que cavar, coletar, construir,
    /// iluminar, brigar e salvar/carregar funcionam. Qualquer falha derruba o processo.
    /// </summary>
    private void RunSmoke()
    {
        // O loop da engine engole exceções de OnUpdate ("frame ignorado") para um script com
        // bug não derrubar o jogo — ótimo em jogo, péssimo no CI: sem este catch, uma
        // verificação falha só imprimiria o erro a cada frame e o processo sairia com 0.
        try
        {
            RunSmokeSteps();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.Exit(1);
        }
    }

    private void RunSmokeSteps()
    {
        switch (_smokeStep)
        {
            case 0 when _elapsed > 0.3f:
                CheckWorldShape();
                _smokeStep++;
                break;

            case 1 when _elapsed > 1.0f:
                CheckPlayerLanded();
                _smokeStep++;
                break;

            case 2 when _elapsed > 1.1f:
                CheckMiningAndBuilding();
                _smokeStep++;
                break;

            case 3 when _elapsed > 1.2f:
                CheckLighting();
                SpawnEnemy(EnemyKind.Slime,
                    (PlayerTransform?.Position ?? Vector2.Zero) + new Vector2(150f, -40f));
                _smokeStep++;
                break;

            case 4 when _elapsed > 1.9f:
                CheckCombat();
                _smokeStep++;
                break;

            case 5 when _elapsed > 2.0f:
                CheckSaveRoundTrip();
                _clock = 23f;                              // anoitece: o spawner deve acordar
                _smokeStep++;
                break;

            case 6 when _elapsed > 7.0f:
                Require(EnemyCount > 0, "nenhum inimigo nasceu em 5 s de noite na superfície.");
                Console.WriteLine("[smoke] ok: mundo procedural, física, mineração, construção, " +
                                  "iluminação, combate, inimigos noturnos e save conferidos.");
                _smokeStep++;
                break;

            case 7 when _elapsed > 7.1f:
                Exit();
                break;
        }
    }

    private void CheckWorldShape()
    {
        Require(Tiles.Width == WorldGen.DefaultWidth && Tiles.Height == WorldGen.DefaultHeight,
            $"mundo com tamanho inesperado: {Tiles.Width}x{Tiles.Height}.");

        int caves = 0, ores = 0, surfaceTiles = 0, underground = 0;

        for (int x = 0; x < Tiles.Width; x += 3)
        {
            int top = Tiles.SurfaceY(x);
            Require(top is > 10 and < 200, $"coluna {x} sem terreno plausível (topo em {top}).");
            surfaceTiles++;

            Require(Tiles.Get(x, Tiles.Height - 1) == TileId.Bedrock,
                $"coluna {x} sem rocha-mãe no fundo.");

            for (int y = top + 10; y < Tiles.Height - 6; y += 2)
            {
                underground++;
                if (Tiles.Get(x, y) == TileId.Empty)
                    caves++;
                if (Tiles.Get(x, y) is TileId.Coal or TileId.IronOre
                    or TileId.GoldOre or TileId.GemOre)
                    ores++;
            }
        }

        Require(surfaceTiles > 100, "amostragem de superfície vazia.");
        Require(caves > underground * 0.02f, $"cavernas de menos: {caves} vazios em {underground} amostras.");
        Require(ores > 20, $"minério de menos: {ores} amostras.");

        int spawnX = (int)Tiles.SpawnTile.X;
        int spawnY = (int)Tiles.SpawnTile.Y;
        Require(!Tiles.IsSolid(spawnX, spawnY) && !Tiles.IsSolid(spawnX, spawnY + 1),
            "ponto de nascimento está dentro da rocha.");
    }

    private void CheckPlayerLanded()
    {
        Require(Player is not null && PlayerTransform is not null, "jogador não foi criado.");
        Require(Player!.Grounded, "jogador não pousou no chão em 1 s.");
        Require(PlayerTransform!.Position.Y < Tiles.WorldHeight,
            "jogador caiu para fora do mundo.");
    }

    private void CheckMiningAndBuilding()
    {
        int x = (int)Tiles.SpawnTile.X + 3;
        int y = Tiles.SurfaceY(x);
        int tile = Tiles.Get(x, y);
        Require(TileDb.IsSolid(tile), $"tile de teste ({x},{y}) devia ser sólido.");

        // Uma picaretada curta racha, mas não quebra.
        Tiles.MineTile(x, y, 0.05f);
        Require(Tiles.Get(x, y) == tile, "bloco quebrou rápido demais.");
        Require(Tiles.DamageAt(x, y) > 0f, "progresso de mineração não foi registrado.");

        int drop = Tiles.MineTile(x, y, 10f);
        Require(Tiles.Get(x, y) == TileId.Empty, "bloco não quebrou com dano suficiente.");
        Require(drop >= 0, "bloco quebrado não dropou item.");

        int before = Backpack.CountOf(drop);
        Require(Collect(drop, 1), "coleta falhou com a mochila vazia.");
        Require(Backpack.CountOf(drop) == before + 1, "item coletado não entrou na mochila.");

        // Constrói de volta pelo mesmo caminho do botão direito.
        Backpack.Selected = 0;
        Backpack.Swap(0, IndexOf(drop));
        Require(Player!.PlaceSelected(x, y), "não conseguiu colocar o bloco de volta.");
        Require(Tiles.IsSolid(x, y), "bloco colocado não ficou sólido.");

        var recipe = Recipes.All[0];
        Backpack.Add(ItemIds.Wood, 5);
        Backpack.Add(ItemIds.Coal, 5);
        Require(Recipes.Craft(Backpack, recipe), "fabricação de tocha falhou com materiais em mãos.");
        Require(Backpack.CountOf(ItemIds.Torch) >= 4, "tocha fabricada não apareceu na mochila.");
    }

    private int IndexOf(int item)
    {
        for (int i = 0; i < Items.Inventory.TotalSlots; i++)
        {
            if (!Backpack.Slots[i].IsEmpty && Backpack.Slots[i].Item == item)
                return i;
        }

        return 0;
    }

    private void CheckLighting()
    {
        int x = (int)Tiles.SpawnTile.X;
        int surface = Tiles.SurfaceY(x);

        _light.Compute(Tiles, x - 20, surface - 20, 60, 90, 1f);

        Require(_light.At(x, surface - 3) > 0.8f, "céu aberto devia estar claro de dia.");
        Require(_light.At(x, surface + 40) < 0.2f, "40 tiles abaixo do chão devia estar escuro.");

        Tiles.SetTile(x, surface + 40, TileId.Torch);
        _light.Compute(Tiles, x - 20, surface - 20, 60, 90, 1f);
        Require(_light.At(x, surface + 40) > 0.5f, "tocha não iluminou o próprio tile.");
        Require(_light.At(x + 3, surface + 40) > 0f, "tocha não espalhou luz para os lados.");
        Tiles.SetTile(x, surface + 40, TileId.Empty);
    }

    private void CheckCombat()
    {
        Require(EnemyCount > 0 || World.Query<EnemyBehavior>().Any(), "inimigo criado não existe.");

        var target = World.Query<Transform, EnemyBehavior>().First();
        HitEnemies(target.C1.Position, 400f, 500f, Vector2.UnitX, _player);

        Require(!World.Query<EnemyBehavior>().Any(), "inimigo não morreu com 500 de dano.");

        int gel = Backpack.CountOf(ItemIds.Gel);
        Require(gel >= 0, "contagem de gosma inválida.");
    }

    private void CheckSaveRoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), "slandsurvivor-smoke.dat");
        int x = (int)Tiles.SpawnTile.X + 6;
        int y = Tiles.SurfaceY(x) + 4;

        Tiles.SetTile(x, y, TileId.Brick);
        SaveWorld(path);

        Tiles.SetTile(x, y, TileId.Empty);
        Require(Tiles.Get(x, y) == TileId.Empty, "tile de teste não foi apagado.");

        Require(LoadWorld(path), "carregar o save falhou.");
        Require(Tiles.Get(x, y) == TileId.Brick,
            $"tile não voltou do save: {TileDb.NameOf(Tiles.Get(x, y))}.");

        File.Delete(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"[smoke] {message}");
    }
}
