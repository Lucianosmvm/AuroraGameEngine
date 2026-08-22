using Aurora.SlandSurvivor.Items;

namespace Aurora.SlandSurvivor.Worlds;

/// <summary>Índices de tile — são também as colunas/linhas do atlas (8 por linha).</summary>
public static class TileId
{
    public const int Empty = -1;

    // linha 0
    public const int Dirt = 0, Grass = 1, Stone = 2, Deepstone = 3,
                     Sand = 4, Sandstone = 5, Snow = 6, Ice = 7;
    // linha 1
    public const int Wood = 8, Leaves = 9, Coal = 10, IronOre = 11,
                     GoldOre = 12, GemOre = 13, Planks = 14, Brick = 15;
    // linha 2
    public const int Torch = 16, Water = 17, Lava = 18, Bedrock = 19,
                     Cactus = 20, Glass = 21, Clay = 22, Mud = 23;
    // linha 4 — paredes de fundo (camada de trás, nunca sólidas)
    public const int WallDirt = 32, WallStone = 33, WallWood = 34,
                     WallBrick = 35, WallSand = 36, WallSnow = 37;

    public const int Count = 40;
    public const int PerRow = 8;
    public const int Size = 16;
}

public enum TileStyle
{
    Speckle, Grass, Ore, Trunk, Leaves, Plank, Brick, Torch, Liquid, Glass, Cactus, Rock,
}

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public Rgb Scale(float f) => new(
        (byte)Math.Clamp(R * f, 0, 255), (byte)Math.Clamp(G * f, 0, 255), (byte)Math.Clamp(B * f, 0, 255));
}

/// <param name="Hardness">Segundos para quebrar com poder de ferramenta 1. Negativo = indestrutível.</param>
/// <param name="Light">Luz emitida, 0–15 (tocha, lava).</param>
/// <param name="MinPower">Poder de picareta necessário (1 = mão/madeira, 3 = ferro).</param>
public sealed record TileDef(
    int Id,
    string Name,
    bool Solid,
    float Hardness,
    int Drop,
    TileStyle Style,
    Rgb Base,
    Rgb Accent,
    int Light = 0,
    int MinPower = 1,
    bool Wall = false,
    bool BlocksLight = true);

/// <summary>
/// Tabela de tiles + geração do tileset em tempo de execução. Nenhum PNG: o atlas
/// (128x80, células de 16x16) é pintado pixel a pixel com um LCG semeado pelo id do tile,
/// então o visual é idêntico em toda execução e o jogo não depende de arte externa.
/// </summary>
public static class TileDb
{
    private static readonly TileDef?[] Defs = new TileDef?[TileId.Count];

    static TileDb()
    {
        Add(new TileDef(TileId.Dirt, "Terra", true, 0.25f, ItemIds.Dirt, TileStyle.Speckle,
            new Rgb(124, 84, 56), new Rgb(96, 62, 40)));
        Add(new TileDef(TileId.Grass, "Grama", true, 0.30f, ItemIds.Dirt, TileStyle.Grass,
            new Rgb(124, 84, 56), new Rgb(92, 168, 74)));
        Add(new TileDef(TileId.Stone, "Pedra", true, 0.75f, ItemIds.Stone, TileStyle.Rock,
            new Rgb(122, 122, 132), new Rgb(92, 92, 102)));
        Add(new TileDef(TileId.Deepstone, "Rocha profunda", true, 1.30f, ItemIds.Deepstone, TileStyle.Rock,
            new Rgb(74, 70, 86), new Rgb(52, 48, 62)));
        Add(new TileDef(TileId.Sand, "Areia", true, 0.25f, ItemIds.Sand, TileStyle.Speckle,
            new Rgb(214, 192, 130), new Rgb(190, 166, 106)));
        Add(new TileDef(TileId.Sandstone, "Arenito", true, 0.70f, ItemIds.Sandstone, TileStyle.Brick,
            new Rgb(186, 156, 104), new Rgb(154, 126, 80)));
        Add(new TileDef(TileId.Snow, "Neve", true, 0.25f, ItemIds.Snow, TileStyle.Speckle,
            new Rgb(232, 238, 246), new Rgb(200, 212, 228)));
        Add(new TileDef(TileId.Ice, "Gelo", true, 0.45f, ItemIds.Ice, TileStyle.Glass,
            new Rgb(150, 204, 232), new Rgb(206, 238, 252)));

        Add(new TileDef(TileId.Wood, "Tronco", true, 0.60f, ItemIds.Wood, TileStyle.Trunk,
            new Rgb(118, 82, 48), new Rgb(88, 58, 32), BlocksLight: false));
        Add(new TileDef(TileId.Leaves, "Folhas", true, 0.15f, ItemIds.Leaves, TileStyle.Leaves,
            new Rgb(60, 138, 62), new Rgb(44, 108, 50), BlocksLight: false));
        Add(new TileDef(TileId.Coal, "Carvão", true, 0.90f, ItemIds.Coal, TileStyle.Ore,
            new Rgb(122, 122, 132), new Rgb(38, 38, 44)));
        Add(new TileDef(TileId.IronOre, "Minério de ferro", true, 1.20f, ItemIds.IronOre, TileStyle.Ore,
            new Rgb(122, 122, 132), new Rgb(190, 148, 116)));
        Add(new TileDef(TileId.GoldOre, "Minério de ouro", true, 1.60f, ItemIds.GoldOre, TileStyle.Ore,
            new Rgb(122, 122, 132), new Rgb(232, 196, 72), MinPower: 2));
        Add(new TileDef(TileId.GemOre, "Cristal", true, 2.40f, ItemIds.Gem, TileStyle.Ore,
            new Rgb(74, 70, 86), new Rgb(120, 226, 232), MinPower: 3));
        Add(new TileDef(TileId.Planks, "Tábuas", true, 0.50f, ItemIds.Planks, TileStyle.Plank,
            new Rgb(166, 122, 74), new Rgb(128, 90, 52)));
        Add(new TileDef(TileId.Brick, "Tijolo de pedra", true, 0.90f, ItemIds.Brick, TileStyle.Brick,
            new Rgb(138, 138, 148), new Rgb(104, 104, 116)));

        Add(new TileDef(TileId.Torch, "Tocha", false, 0.05f, ItemIds.Torch, TileStyle.Torch,
            new Rgb(122, 84, 48), new Rgb(255, 196, 92), Light: 13, BlocksLight: false));
        Add(new TileDef(TileId.Water, "Água", false, -1f, ItemIds.None, TileStyle.Liquid,
            new Rgb(52, 106, 200), new Rgb(96, 158, 236), BlocksLight: false));
        Add(new TileDef(TileId.Lava, "Lava", false, -1f, ItemIds.None, TileStyle.Liquid,
            new Rgb(206, 80, 24), new Rgb(250, 168, 52), Light: 9, BlocksLight: false));
        Add(new TileDef(TileId.Bedrock, "Rocha-mãe", true, -1f, ItemIds.None, TileStyle.Rock,
            new Rgb(46, 42, 52), new Rgb(28, 26, 34)));
        Add(new TileDef(TileId.Cactus, "Cacto", true, 0.40f, ItemIds.Cactus, TileStyle.Cactus,
            new Rgb(66, 138, 72), new Rgb(46, 106, 54)));
        Add(new TileDef(TileId.Glass, "Vidro", true, 0.30f, ItemIds.Glass, TileStyle.Glass,
            new Rgb(178, 214, 232), new Rgb(228, 244, 252), BlocksLight: false));
        Add(new TileDef(TileId.Clay, "Argila", true, 0.35f, ItemIds.Clay, TileStyle.Speckle,
            new Rgb(158, 106, 96), new Rgb(132, 84, 76)));
        Add(new TileDef(TileId.Mud, "Lama", true, 0.30f, ItemIds.Mud, TileStyle.Speckle,
            new Rgb(84, 62, 46), new Rgb(64, 46, 34)));

        // Paredes: mesmo desenho do bloco, escurecido. Nunca sólidas — só dão fundo à caverna.
        Add(new TileDef(TileId.WallDirt, "Parede de terra", false, -1f, ItemIds.None, TileStyle.Speckle,
            new Rgb(124, 84, 56), new Rgb(96, 62, 40), Wall: true, BlocksLight: false));
        Add(new TileDef(TileId.WallStone, "Parede de pedra", false, -1f, ItemIds.None, TileStyle.Rock,
            new Rgb(122, 122, 132), new Rgb(92, 92, 102), Wall: true, BlocksLight: false));
        Add(new TileDef(TileId.WallWood, "Parede de madeira", false, -1f, ItemIds.None, TileStyle.Plank,
            new Rgb(166, 122, 74), new Rgb(128, 90, 52), Wall: true, BlocksLight: false));
        Add(new TileDef(TileId.WallBrick, "Parede de tijolo", false, -1f, ItemIds.None, TileStyle.Brick,
            new Rgb(138, 138, 148), new Rgb(104, 104, 116), Wall: true, BlocksLight: false));
        Add(new TileDef(TileId.WallSand, "Parede de arenito", false, -1f, ItemIds.None, TileStyle.Brick,
            new Rgb(186, 156, 104), new Rgb(154, 126, 80), Wall: true, BlocksLight: false));
        Add(new TileDef(TileId.WallSnow, "Parede de gelo", false, -1f, ItemIds.None, TileStyle.Speckle,
            new Rgb(196, 214, 230), new Rgb(168, 186, 206), Wall: true, BlocksLight: false));
    }

    private static void Add(TileDef def) => Defs[def.Id] = def;

    public static TileDef? Get(int id) => id >= 0 && id < TileId.Count ? Defs[id] : null;

    public static bool IsSolid(int id) => Get(id)?.Solid == true;

    public static bool BlocksLight(int id) => Get(id) is { BlocksLight: true };

    public static int LightOf(int id) => Get(id)?.Light ?? 0;

    public static string NameOf(int id) => Get(id)?.Name ?? "?";

    /// <summary>Ids que o <c>Tilemap</c> da engine deve tratar como parede sólida.</summary>
    public static IEnumerable<int> SolidIds()
    {
        for (int i = 0; i < TileId.Count; i++)
        {
            if (Defs[i] is { Solid: true })
                yield return i;
        }
    }

    // ---------------------------------------------------------------------
    //  Atlas
    // ---------------------------------------------------------------------

    public const int AtlasWidth = TileId.PerRow * TileId.Size;                  // 128
    public const int AtlasHeight = TileId.Count / TileId.PerRow * TileId.Size;   // 80

    /// <summary>Pinta o tileset inteiro em RGBA (4 bytes por pixel, linha a linha).</summary>
    public static byte[] BuildAtlas()
    {
        var pixels = new byte[AtlasWidth * AtlasHeight * 4];

        for (int id = 0; id < TileId.Count; id++)
        {
            if (Defs[id] is not { } def)
                continue;

            int ox = id % TileId.PerRow * TileId.Size;
            int oy = id / TileId.PerRow * TileId.Size;
            PaintTile(pixels, ox, oy, def);
        }

        return pixels;
    }

    private static void PaintTile(byte[] pixels, int ox, int oy, TileDef def)
    {
        uint rng = (uint)(def.Id * 2654435761u + 12345u);
        float dim = def.Wall ? 0.42f : 1f;

        for (int y = 0; y < TileId.Size; y++)
        {
            for (int x = 0; x < TileId.Size; x++)
            {
                var (color, alpha) = Sample(def, x, y, ref rng);
                Put(pixels, ox + x, oy + y, color.Scale(dim), alpha);
            }
        }
    }

    private static (Rgb Color, byte Alpha) Sample(TileDef def, int x, int y, ref uint rng)
    {
        float n = NextFloat(ref rng);
        const int last = TileId.Size - 1;

        switch (def.Style)
        {
            case TileStyle.Grass:
                // Terra com faixa de grama no topo e algumas mechas descendo.
                int bladeDepth = 3 + (int)(NextFloat(ref rng) * 2.5f);
                if (y < bladeDepth)
                    return (def.Accent.Scale(0.92f + n * 0.2f), 255);
                if (y == bladeDepth && n > 0.55f)
                    return (def.Accent.Scale(0.8f), 255);
                return (def.Base.Scale(0.88f + n * 0.22f), 255);

            case TileStyle.Ore:
                var rock = def.Base.Scale(0.86f + n * 0.24f);
                if (OreMask(def.Id, x, y))
                    return (def.Accent.Scale(0.85f + n * 0.3f), 255);
                return (Edge(rock, x, y), 255);

            case TileStyle.Trunk:
                bool bark = x % 5 == 0 || (x % 5 == 3 && (y / 3 & 1) == 0);
                return (bark ? def.Accent.Scale(0.9f + n * 0.15f) : def.Base.Scale(0.9f + n * 0.2f), 255);

            case TileStyle.Leaves:
                if (n < 0.10f)
                    return (def.Base, 0);                       // buraco: deixa ver o fundo
                bool clump = (x + y * 3) % 7 < 3;
                return ((clump ? def.Base : def.Accent).Scale(0.85f + n * 0.3f), 255);

            case TileStyle.Plank:
                bool seam = y % 5 == 4 || x == (y / 5 % 2 == 0 ? 4 : 11);
                return (seam ? def.Accent.Scale(0.72f) : def.Base.Scale(0.92f + n * 0.14f), 255);

            case TileStyle.Brick:
                int shift = (y / 4 & 1) == 0 ? 0 : 4;
                bool mortar = y % 4 == 3 || (x + shift) % 8 == 7;
                return (mortar ? def.Accent.Scale(0.7f) : def.Base.Scale(0.9f + n * 0.16f), 255);

            case TileStyle.Torch:
                bool stick = x >= 7 && x <= 8 && y >= 6;
                bool flame = x >= 6 && x <= 9 && y >= 2 && y <= 6
                             && (x - 7.5f) * (x - 7.5f) * 1.6f + (y - 4.5f) * (y - 4.5f) < 7f;
                if (flame)
                    return (def.Accent.Scale(0.85f + n * 0.3f), 255);
                if (stick)
                    return (def.Base.Scale(0.9f + n * 0.15f), 255);
                return (def.Base, 0);

            case TileStyle.Liquid:
                bool crest = (y + x / 4) % 6 == 0;
                byte liquidAlpha = def.Id == TileId.Lava ? (byte)235 : (byte)165;
                return (crest ? def.Accent.Scale(0.95f + n * 0.1f) : def.Base.Scale(0.9f + n * 0.2f), liquidAlpha);

            case TileStyle.Glass:
                bool frame = x == 0 || y == 0 || x == last || y == last;
                bool shine = x - y is 3 or 4;
                if (frame)
                    return (def.Accent, 210);
                if (shine)
                    return (def.Accent, 150);
                return (def.Base, 90);

            case TileStyle.Cactus:
                if (x < 3 || x > 12)
                    return (def.Base, 0);
                bool ridge = x is 5 or 9;
                bool spine = y % 5 == 2 && (x == 3 || x == 12);
                if (spine)
                    return (new Rgb(226, 226, 200), 255);
                return ((ridge ? def.Accent : def.Base).Scale(0.9f + n * 0.2f), 255);

            case TileStyle.Rock:
                // Manchas grandes + granulado fino: lê como pedra, não como chuvisco de TV.
                float blob = (x / 4 + y / 5) % 2 == 0 ? 1.06f : 0.94f;
                return (Edge(def.Base.Scale(blob * (0.9f + n * 0.2f)), x, y), 255);

            default:
                return (Edge(def.Base.Scale(0.88f + n * 0.24f), x, y), 255);
        }
    }

    /// <summary>Sombra na borda de baixo/direita e brilho em cima: dá volume ao bloco liso.</summary>
    private static Rgb Edge(Rgb color, int x, int y)
    {
        const int last = TileId.Size - 1;
        if (y == 0 || x == 0)
            return color.Scale(1.14f);
        if (y >= last - 1 || x >= last - 1)
            return color.Scale(0.82f);
        return color;
    }

    /// <summary>Máscara dos veios de minério: 3 bolhas em posições fixas por tipo de tile.</summary>
    private static bool OreMask(int id, int x, int y)
    {
        for (int i = 0; i < 3; i++)
        {
            float cx = 3f + Noise.HashPoint(id * 31 + i, i, 1, 3) * 10f;
            float cy = 3f + Noise.HashPoint(id * 31 + i, i, 2, 5) * 10f;
            float r = 2.1f + Noise.HashPoint(id * 31 + i, i, 3, 7) * 1.7f;
            float dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r * r)
                return true;
        }

        return false;
    }

    private static void Put(byte[] pixels, int x, int y, Rgb color, byte alpha)
    {
        int i = (y * AtlasWidth + x) * 4;
        pixels[i + 0] = color.R;
        pixels[i + 1] = color.G;
        pixels[i + 2] = color.B;
        pixels[i + 3] = alpha;
    }

    /// <summary>LCG minúsculo — determinístico e independente da implementação de Random.</summary>
    private static float NextFloat(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return ((state >> 8) & 0xFF_FFFF) / 16777216f;
    }
}
