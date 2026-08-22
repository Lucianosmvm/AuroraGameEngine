namespace Aurora.SlandSurvivor.Worlds;

public enum Biome { Forest, Desert, Snow }

/// <summary>Resultado bruto da geração: as duas camadas de tiles e os metadados que o jogo
/// usa depois (bioma por coluna, altura do terreno, ponto de nascimento).</summary>
public sealed class GeneratedWorld
{
    public required int Seed { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int[] Foreground { get; init; }
    public required int[] Background { get; init; }
    public required int[] SurfaceY { get; init; }
    public required Biome[] Biomes { get; init; }
    public required int SpawnX { get; init; }
    public required int SpawnY { get; init; }
}

/// <summary>
/// Gerador procedural do mundo. Tudo sai da seed: mesma seed, mesmo mundo — não existe
/// <see cref="Random"/> aqui, só hash das coordenadas (ver <see cref="Noise"/>).
///
/// <para>Ordem das etapas (cada uma só depende das anteriores):</para>
/// <list type="number">
///   <item>relevo e bioma por coluna (ruído 1D de baixa frequência);</item>
///   <item>preenchimento vertical: superfície, camada de terra, pedra, rocha profunda, rocha-mãe;</item>
///   <item>paredes de fundo (só abaixo do terreno — é o que faz caverna parecer caverna);</item>
///   <item>cavernas: túneis longos (ruído de crista) + cavernas grandes no fundo;</item>
///   <item>bolsões de argila e veios de minério, cada um na sua faixa de profundidade;</item>
///   <item>líquidos: lagos na superfície e poças de água/lava em cavernas, com teste de vazamento;</item>
///   <item>vegetação (árvores, cactos) e ruínas de tijolo com tocha.</item>
/// </list>
/// </summary>
public static class WorldGen
{
    public const int DefaultWidth = 1200;
    public const int DefaultHeight = 300;

    private const int SkyRows = 34;          // faixa de céu acima do pico mais alto
    private const int SurfaceBase = 74;      // altura média do terreno, em tiles
    private const int DeepStart = 175;       // a partir daqui é rocha profunda
    private const int BedrockRows = 4;

    public static GeneratedWorld Generate(int seed, int width = DefaultWidth, int height = DefaultHeight)
    {
        width = Math.Max(200, width);
        height = Math.Max(120, height);

        var fg = new int[width * height];
        var bg = new int[width * height];
        Array.Fill(fg, TileId.Empty);
        Array.Fill(bg, TileId.Empty);

        var surface = new int[width];
        var biomes = new Biome[width];

        ShapeTerrain(seed, width, height, fg, bg, surface, biomes);
        CarveCaves(seed, width, height, fg, surface);
        ScatterPockets(seed, width, height, fg, surface);
        PlaceLiquids(seed, width, height, fg, surface);
        Decorate(seed, width, height, fg, bg, surface, biomes);

        int spawnX = FindSpawnColumn(fg, width, surface);
        int spawnY = ClearSpawnArea(fg, width, spawnX, surface[spawnX]);

        return new GeneratedWorld
        {
            Seed = seed,
            Width = width,
            Height = height,
            Foreground = fg,
            Background = bg,
            SurfaceY = surface,
            Biomes = biomes,
            SpawnX = spawnX,
            SpawnY = spawnY,
        };
    }

    /// <summary>
    /// Procura, a partir do meio do mundo, uma coluna boa para nascer: chão firme por vários
    /// tiles (nada de cair direto numa caverna que encosta na superfície) e sem lago logo
    /// acima (nascer boiando não deixa o jogador nem pular). Cai de volta no meio se, por
    /// azar de seed, nenhuma coluna próxima servir.
    /// </summary>
    private static int FindSpawnColumn(int[] fg, int width, int[] surface)
    {
        int center = width / 2;

        for (int offset = 0; offset < 250; offset++)
        {
            for (int direction = 1; direction >= -1; direction -= 2)
            {
                int x = center + direction * offset;
                if (x < 6 || x >= width - 6)
                    continue;

                if (IsGoodSpawnColumn(fg, width, x, surface[x]))
                    return x;
            }
        }

        return center;
    }

    private static bool IsGoodSpawnColumn(int[] fg, int width, int x, int groundY)
    {
        for (int y = groundY; y <= groundY + 6; y++)
        {
            if (!TileDb.IsSolid(fg[y * width + x]))
                return false;                               // caverna logo abaixo do chão
        }

        for (int y = Math.Max(0, groundY - 5); y < groundY; y++)
        {
            if (fg[y * width + x] is TileId.Water or TileId.Lava)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Abre uma clareira acima do chão no ponto de nascimento e devolve a linha onde o
    /// jogador aparece. A vegetação é espalhada por ruído e não sabe onde é o spawn: sem
    /// isso, uma árvore bem no meio do mundo faria o jogador nascer dentro do tronco.
    /// </summary>
    private static int ClearSpawnArea(int[] fg, int width, int spawnX, int groundY)
    {
        for (int y = Math.Max(1, groundY - 5); y < groundY; y++)
        {
            for (int x = Math.Max(0, spawnX - 2); x <= Math.Min(width - 1, spawnX + 2); x++)
                fg[y * width + x] = TileId.Empty;
        }

        return Math.Max(1, groundY - 2);
    }

    // -------------------------------------------------------------------------
    //  1 + 2 + 3 — relevo, colunas e paredes
    // -------------------------------------------------------------------------

    private static void ShapeTerrain(int seed, int width, int height,
        int[] fg, int[] bg, int[] surface, Biome[] biomes)
    {
        int bedrockStart = height - BedrockRows;

        for (int x = 0; x < width; x++)
        {
            // Relevo: uma onda larga de colinas + um detalhe fino para não ficar liso demais.
            float hills = Noise.Fbm1D(seed + 1, x * 0.006f, 4) * 2f - 1f;
            float detail = Noise.Fbm1D(seed + 2, x * 0.035f, 3) * 2f - 1f;
            int top = SurfaceBase + (int)MathF.Round(hills * 22f + detail * 4f);
            top = Math.Clamp(top, SkyRows, DeepStart - 40);

            var biome = BiomeAt(seed, x);
            biomes[x] = biome;
            surface[x] = top;

            int soilDepth = 5 + (int)(Noise.Fbm1D(seed + 3, x * 0.05f, 2) * 7f);
            int stoneStart = top + soilDepth;

            for (int y = top; y < height; y++)
            {
                int index = y * width + x;

                fg[index] = y >= bedrockStart && y >= height - BedrockRows - JaggedBedrock(seed, x)
                    ? TileId.Bedrock
                    : y >= DeepStart ? TileId.Deepstone
                    : y >= stoneStart ? TileId.Stone
                    : SoilTile(biome, y == top, y - top);

                // Parede de fundo começa 3 tiles abaixo do terreno: assim uma caverna que
                // encosta na superfície ainda mostra céu na boca, não parede.
                if (y >= top + 3)
                    bg[index] = WallFor(biome, y, stoneStart);
            }
        }
    }

    private static Biome BiomeAt(int seed, int x)
    {
        float t = Noise.Fbm1D(seed + 500, x * 0.0016f, 3);
        return t < 0.36f ? Biome.Desert : t > 0.64f ? Biome.Snow : Biome.Forest;
    }

    private static int SoilTile(Biome biome, bool isTop, int depth) => biome switch
    {
        Biome.Desert => depth < 4 ? TileId.Sand : TileId.Sandstone,
        Biome.Snow => isTop ? TileId.Snow : depth < 3 ? TileId.Snow : TileId.Dirt,
        _ => isTop ? TileId.Grass : TileId.Dirt,
    };

    private static int WallFor(Biome biome, int y, int stoneStart)
    {
        if (y >= stoneStart)
            return TileId.WallStone;

        return biome switch
        {
            Biome.Desert => TileId.WallSand,
            Biome.Snow => TileId.WallSnow,
            _ => TileId.WallDirt,
        };
    }

    /// <summary>Rocha-mãe com borda irregular — piso intransponível sem parecer uma régua.</summary>
    private static int JaggedBedrock(int seed, int x) => (int)(Noise.Fbm1D(seed + 9, x * 0.3f, 2) * 3f);

    // -------------------------------------------------------------------------
    //  4 — cavernas
    // -------------------------------------------------------------------------

    private static void CarveCaves(int seed, int width, int height, int[] fg, int[] surface)
    {
        int bedrockStart = height - BedrockRows - 3;

        for (int x = 0; x < width; x++)
        {
            int top = surface[x];

            for (int y = top + 5; y < bedrockStart; y++)
            {
                int index = y * width + x;
                if (fg[index] == TileId.Empty)
                    continue;

                // Perto da superfície a escavação entra de leve (senão o chão vira queijo);
                // depois de ~24 tiles de profundidade vale integral.
                float depthFade = Math.Clamp((y - top - 5) / 24f, 0f, 1f);

                // Túneis: ruído de crista alongado no eixo X vira corredor, não bolha.
                float tunnel = Noise.Ridge(seed + 31, x * 0.022f, y * 0.055f, 3);
                bool isTunnel = tunnel > 0.90f - depthFade * 0.05f;

                // Salões: só no fundo, onde é gostoso encontrar espaço aberto.
                float hall = Noise.Fbm(seed + 47, x * 0.02f, y * 0.028f, 3);
                bool isHall = y > DeepStart - 40 && hall > 0.70f - depthFade * 0.03f;

                if (isTunnel || isHall)
                    fg[index] = TileId.Empty;
            }
        }
    }

    // -------------------------------------------------------------------------
    //  5 — argila e minérios
    // -------------------------------------------------------------------------

    private readonly record struct Vein(int Tile, int MinDepth, int MaxDepth, float Density, float Radius, int Salt);

    private static readonly Vein[] Veins =
    [
        new(TileId.Clay,     4,   40,  0.0022f, 2.6f, 11),
        new(TileId.Coal,     8,  999,  0.0018f, 2.2f, 13),
        new(TileId.IronOre, 16,  999,  0.0013f, 1.9f, 17),
        new(TileId.GoldOre, 60,  999,  0.0007f, 1.7f, 19),
        new(TileId.GemOre, 118,  999,  0.0005f, 1.5f, 23),
    ];

    private static void ScatterPockets(int seed, int width, int height, int[] fg, int[] surface)
    {
        foreach (var vein in Veins)
        {
            for (int x = 2; x < width - 2; x++)
            {
                int from = surface[x] + vein.MinDepth;
                int to = Math.Min(height - BedrockRows - 2, surface[x] + vein.MaxDepth);

                for (int y = from; y < to; y++)
                {
                    if (!IsStoneLike(fg[y * width + x]))
                        continue;

                    if (Noise.HashPoint(seed, x, y, vein.Salt) >= vein.Density)
                        continue;

                    float radius = vein.Radius * (0.7f + Noise.HashPoint(seed, x, y, vein.Salt + 1) * 0.6f);
                    PaintBlob(fg, width, height, x, y, radius, vein.Tile);
                }
            }
        }
    }

    private static bool IsStoneLike(int tile)
        => tile is TileId.Stone or TileId.Deepstone or TileId.Dirt or TileId.Sandstone;

    /// <summary>Mancha aproximadamente circular, só sobre rocha/terra (nunca fecha caverna).</summary>
    private static void PaintBlob(int[] fg, int width, int height, int cx, int cy, float radius, int tile)
    {
        int r = (int)MathF.Ceiling(radius);

        for (int y = cy - r; y <= cy + r; y++)
        {
            if (y < 0 || y >= height)
                continue;

            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || x >= width)
                    continue;

                float dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > radius * radius)
                    continue;

                int index = y * width + x;
                if (IsStoneLike(fg[index]))
                    fg[index] = tile;
            }
        }
    }

    // -------------------------------------------------------------------------
    //  6 — líquidos
    // -------------------------------------------------------------------------

    private static void PlaceLiquids(int seed, int width, int height, int[] fg, int[] surface)
    {
        // Lagos de superfície: tenta encher as depressões do relevo.
        for (int x = 12; x < width - 12; x += 7)
        {
            if (Noise.HashPoint(seed, x, 0, 61) > 0.16f)
                continue;

            if (surface[x] <= surface[x - 6] || surface[x] <= surface[x + 6])
                continue;                                   // não é fundo de vale

            FillPool(fg, width, height, x, surface[x] - 1, TileId.Water, maxRows: 5, maxSpan: 26);
        }

        // Poças subterrâneas: água em profundidade média, lava no fundo.
        for (int x = 8; x < width - 8; x += 3)
        {
            for (int y = surface[x] + 30; y < height - BedrockRows - 3; y += 4)
            {
                if (Noise.HashPoint(seed, x, y, 71) > 0.010f)
                    continue;

                if (fg[y * width + x] != TileId.Empty || fg[(y + 1) * width + x] == TileId.Empty)
                    continue;                               // precisa de chão sob a poça

                int liquid = y > DeepStart + 20 ? TileId.Lava : TileId.Water;
                FillPool(fg, width, height, x, y, liquid, maxRows: 4, maxSpan: 14);
            }
        }
    }

    /// <summary>
    /// Enche uma bacia linha a linha, de baixo para cima. Cada linha só é aceita se estiver
    /// fechada dos dois lados dentro de <paramref name="maxSpan"/> e apoiada em piso — é esse
    /// teste que impede a água de escorrer pelo mundo inteiro por um buraco de um tile.
    /// </summary>
    private static void FillPool(int[] fg, int width, int height, int x0, int y0, int liquid,
        int maxRows, int maxSpan)
    {
        Span<int> rowStart = stackalloc int[8];
        Span<int> rowEnd = stackalloc int[8];
        maxRows = Math.Min(maxRows, 8);
        int rows = 0;

        for (int r = 0; r < maxRows; r++)
        {
            int y = y0 - r;
            if (y <= 1 || y >= height - 1)
                break;

            if (!TryScanRow(fg, width, height, x0, y, maxSpan, out int left, out int right))
                break;

            rowStart[rows] = left;
            rowEnd[rows] = right;
            rows++;
        }

        for (int r = 0; r < rows; r++)
        {
            int y = y0 - r;
            for (int x = rowStart[r]; x <= rowEnd[r]; x++)
                fg[y * width + x] = liquid;
        }
    }

    /// <summary>Uma linha serve se for vazia, tiver piso embaixo e bater em parede dos dois lados.</summary>
    private static bool TryScanRow(int[] fg, int width, int height, int x0, int y, int maxSpan,
        out int left, out int right)
    {
        left = right = x0;

        if (fg[y * width + x0] != TileId.Empty)
            return false;

        for (int dir = -1; dir <= 1; dir += 2)
        {
            int x = x0;
            bool walled = false;

            for (int step = 0; step <= maxSpan; step++)
            {
                int next = x + dir;
                if (next < 1 || next >= width - 1)
                    return false;

                if (fg[y * width + next] != TileId.Empty)
                {
                    walled = true;
                    break;
                }

                // Piso sob a próxima célula: sem isso a água vazaria para o andar de baixo.
                if (fg[(y + 1) * width + next] == TileId.Empty)
                    return false;

                x = next;
            }

            if (!walled)
                return false;

            if (dir < 0)
                left = x;
            else
                right = x;
        }

        return true;
    }

    // -------------------------------------------------------------------------
    //  7 — vegetação e ruínas
    // -------------------------------------------------------------------------

    private static void Decorate(int seed, int width, int height,
        int[] fg, int[] bg, int[] surface, Biome[] biomes)
    {
        int lastFeature = -8;

        for (int x = 4; x < width - 4; x++)
        {
            int top = surface[x];
            int groundIndex = top * width + x;

            if (x - lastFeature >= 4 && fg[groundIndex] == TileId.Grass
                && Noise.HashPoint(seed, x, top, 83) < 0.22f)
            {
                PlantTree(seed, fg, width, height, x, top);
                lastFeature = x;
            }
            else if (x - lastFeature >= 5 && biomes[x] == Biome.Desert && fg[groundIndex] == TileId.Sand
                     && Noise.HashPoint(seed, x, top, 89) < 0.10f)
            {
                int tall = 2 + (int)(Noise.HashPoint(seed, x, top, 91) * 3f);
                for (int i = 1; i <= tall; i++)
                {
                    int y = top - i;
                    if (y > 0 && fg[y * width + x] == TileId.Empty)
                        fg[y * width + x] = TileId.Cactus;
                }

                lastFeature = x;
            }
        }

        // Ruínas subterrâneas: uma sala de tijolo com tocha a cada ~200 colunas.
        for (int x = 60; x < width - 60; x += 40)
        {
            if (Noise.HashPoint(seed, x, 7, 97) > 0.22f)
                continue;

            int y = surface[x] + 26 + (int)(Noise.HashPoint(seed, x, 8, 101) * 60f);
            if (y > height - BedrockRows - 12)
                continue;

            BuildRuin(fg, bg, width, height, x, y);
        }
    }

    private static void PlantTree(int seed, int[] fg, int width, int height, int x, int groundY)
    {
        int trunk = 4 + (int)(Noise.HashPoint(seed, x, groundY, 103) * 5f);
        int topY = groundY - trunk;
        if (topY < 3)
            return;

        for (int y = groundY - 1; y >= topY; y--)
        {
            if (fg[y * width + x] != TileId.Empty)
                return;                                     // encostou em algo: não planta
        }

        for (int y = groundY - 1; y >= topY; y--)
            fg[y * width + x] = TileId.Wood;

        float radius = 2.2f + Noise.HashPoint(seed, x, groundY, 107) * 1.4f;
        int r = (int)MathF.Ceiling(radius);

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int lx = x + dx, ly = topY + dy;
                if (lx < 0 || lx >= width || ly < 1 || ly >= height)
                    continue;

                // Elipse achatada: copa larga e baixa lê melhor que uma bola perfeita.
                if (dx * dx + dy * dy * 1.8f > radius * radius)
                    continue;

                int index = ly * width + lx;
                if (fg[index] == TileId.Empty)
                    fg[index] = TileId.Leaves;
            }
        }
    }

    private static void BuildRuin(int[] fg, int[] bg, int width, int height, int cx, int cy)
    {
        const int halfW = 5, halfH = 3;

        for (int y = cy - halfH; y <= cy + halfH; y++)
        {
            for (int x = cx - halfW; x <= cx + halfW; x++)
            {
                if (x < 1 || x >= width - 1 || y < 1 || y >= height - 1)
                    continue;

                int index = y * width + x;
                bool shell = x == cx - halfW || x == cx + halfW || y == cy - halfH || y == cy + halfH;

                fg[index] = shell ? TileId.Brick : TileId.Empty;
                bg[index] = TileId.WallBrick;
            }
        }

        // Duas tochas presas na parede, uma de cada lado — a sala nasce iluminada.
        int torchY = cy - halfH + 1;
        SetIfInside(fg, width, height, cx - halfW + 1, torchY, TileId.Torch);
        SetIfInside(fg, width, height, cx + halfW - 1, torchY, TileId.Torch);
    }

    private static void SetIfInside(int[] fg, int width, int height, int x, int y, int tile)
    {
        if (x >= 0 && x < width && y >= 0 && y < height)
            fg[y * width + x] = tile;
    }
}
