using Silk.NET.OpenGL;

namespace Aurora.Runtime.Graphics;

/// <summary>
/// Gera em código o tileset de um líquido animado (água, lava, sangue, pântano) para o
/// <see cref="Ecs.Components.Tilemap"/> — sem arte externa, sem shader.
///
/// <para><b>Layout do atlas:</b> 16 colunas × <c>Frames</c> linhas. A coluna é a máscara de
/// vizinhança (N=1, L=2, S=4, O=8; bit ligado = o vizinho também é líquido, ou seja, aquele
/// lado <em>não</em> tem margem) e a linha é o frame da animação. Isso casa exatamente com
/// <c>Tilemap.Autotile()</c> (que escreve a máscara como índice) e com
/// <c>Tilemap.AnimationFrames</c> (que troca a linha ao longo do tempo).</para>
///
/// <para><b>Uso típico:</b></para>
/// <code>
/// var texture = LiquidTileset.CreateTexture(Gl, LiquidStyle.Water());
/// var lake = World.CreateEntity("Lago");
/// lake.Add(new Transform(origem));
/// var map = lake.Add(new Tilemap
/// {
///     Tileset = texture, TileWidth = 32, TileHeight = 32,
///     Width = 20, Height = 12, Layer = 1,
///     AnimationFrames = 4, AnimationFrameDuration = 0.18f,
///     AnimationColumns = LiquidTileset.Columns,
/// });
/// map.Fill(3, 3, 6, 4, 0);   // pinta a lagoa com qualquer índice >= 0
/// map.Autotile();            // vira máscara: miolo, margens e cantos
/// </code>
///
/// <para>A onda usa só frequências inteiras sobre o tile, então tiles vizinhos costuram sem
/// emenda e o último frame emenda no primeiro (loop perfeito).</para>
/// </summary>
public static class LiquidTileset
{
    /// <summary>Colunas do atlas — uma por máscara de vizinhança (0–15).</summary>
    public const int Columns = 16;

    /// <summary>Máscara em que os quatro vizinhos são líquido — miolo do lago, sem margem.</summary>
    public const int Center = 15;

    private const float Tau = MathF.PI * 2f;

    /// <summary>Índice do tile para uma máscara de vizinhança num frame.</summary>
    public static int Index(int mask, int frame = 0) => frame * Columns + (mask & 15);

    public static int AtlasWidth(LiquidStyle style) => Columns * style.TileSize;

    public static int AtlasHeight(LiquidStyle style) => Math.Max(1, style.Frames) * style.TileSize;

    /// <summary>Textura pronta pra jogar no <c>Tilemap.Tileset</c>.</summary>
    public static Texture2D CreateTexture(GL gl, LiquidStyle style)
        => Texture2D.FromPixels(gl, AtlasWidth(style), AtlasHeight(style), BuildAtlas(style));

    /// <summary>Grava o atlas como PNG — é assim que o tileset entra na pasta Assets e
    /// aparece na paleta de pintura do editor.</summary>
    public static void SavePng(string path, LiquidStyle style)
        => PngWriter.Write(path, AtlasWidth(style), AtlasHeight(style), BuildAtlas(style));

    /// <summary>Pinta o atlas inteiro em RGBA (4 bytes por pixel, linha a linha).</summary>
    public static byte[] BuildAtlas(LiquidStyle style)
    {
        int size = Math.Max(2, style.TileSize);
        int frames = Math.Max(1, style.Frames);
        int width = Columns * size;
        var pixels = new byte[width * frames * size * 4];

        for (int frame = 0; frame < frames; frame++)
        {
            float time = frame / (float)frames;

            for (int mask = 0; mask < Columns; mask++)
            {
                int ox = mask * size;
                int oy = frame * size;

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var (color, alpha) = Sample(style, size, mask, time, x, y);
                        Put(pixels, width, ox + x, oy + y, color, alpha);
                    }
                }
            }
        }

        return pixels;
    }

    private static (Color Color, float Alpha) Sample(LiquidStyle style, int size, int mask, float time, int x, int y)
    {
        float u = (x + 0.5f) / size;
        float v = (y + 0.5f) / size;

        // Faixas horizontais onduladas — a leitura clássica de água vista de cima. A senoide
        // de fora desenha as faixas ao longo de v; a de dentro entorta essas faixas ao longo
        // de u pra não virarem régua. Um "swell" diagonal lento quebra o resto da simetria.
        // Todas as frequências são inteiras em u, v e no tempo: por isso o tile vizinho
        // costura sem emenda e o último frame emenda no primeiro.
        float wobble = MathF.Sin(Tau * (style.WavesX * u - time));
        float bands = MathF.Sin(Tau * (style.WavesY * v + time + 0.22f * wobble));
        float swell = MathF.Sin(Tau * (u + v - time));

        float level = 0.5f + (bands * 0.36f + swell * 0.16f) * style.Contrast
                    + (Hash(x, y, style.Seed) - 0.5f) * 0.05f;      // granulado fino
        level = Math.Clamp(level, 0f, 1f);

        var color = Lerp(style.Deep, style.Shallow, level);
        if (level > 0.84f)
            color = Lerp(color, style.Crest, (level - 0.84f) / 0.16f * 0.9f);

        // Margem: cada lado sem vizinho líquido projeta uma faixa da cor de borda, com a
        // largura ondulando ao longo da borda (e no tempo) pra não virar um traço reto.
        float edge = 0f;
        if ((mask & 1) == 0) edge = MathF.Max(edge, Band(style, size, time, y, x));
        if ((mask & 2) == 0) edge = MathF.Max(edge, Band(style, size, time, size - 1 - x, y));
        if ((mask & 4) == 0) edge = MathF.Max(edge, Band(style, size, time, size - 1 - y, x));
        if ((mask & 8) == 0) edge = MathF.Max(edge, Band(style, size, time, x, y));

        // Dois degraus em vez de gradiente: pixel art fica mais legível com a borda marcada.
        if (edge > 0.58f)
            color = style.Edge;
        else if (edge > 0.24f)
            color = Lerp(color, style.Edge, 0.45f);

        float alpha = IsOutsideCorner(style, size, mask, x, y) ? 0f : style.Opacity;
        return (color, alpha);
    }

    /// <summary>Intensidade da margem a <paramref name="distance"/> pixels da borda, com a
    /// largura ondulando conforme <paramref name="along"/> (posição ao longo da borda).</summary>
    private static float Band(LiquidStyle style, int size, float time, int distance, int along)
    {
        if (style.EdgeWidth <= 0f || distance < 0)
            return 0f;

        float wobble = 0.5f + 0.5f * MathF.Sin(Tau * (2f * ((along + 0.5f) / size) + time));
        float band = size * style.EdgeWidth * (0.7f + 0.6f * wobble);
        return band <= 0f ? 0f : Math.Clamp(1f - distance / band, 0f, 1f);
    }

    /// <summary>
    /// True quando o pixel cai fora do arco do canto — só vale nos cantos em que os DOIS
    /// lados vizinhos são terra, que é onde a lagoa precisa arredondar em vez de fechar
    /// num quadrado.
    /// </summary>
    private static bool IsOutsideCorner(LiquidStyle style, int size, int mask, int x, int y)
    {
        float r = size * style.CornerRadius;
        if (r <= 0.5f)
            return false;

        bool north = (mask & 1) == 0, east = (mask & 2) == 0;
        bool south = (mask & 4) == 0, west = (mask & 8) == 0;

        float px = x + 0.5f, py = y + 0.5f;

        if (north && west && px < r && py < r) return Distance(px, py, r, r) > r;
        if (north && east && px > size - r && py < r) return Distance(px, py, size - r, r) > r;
        if (south && east && px > size - r && py > size - r) return Distance(px, py, size - r, size - r) > r;
        if (south && west && px < r && py > size - r) return Distance(px, py, r, size - r) > r;

        return false;
    }

    private static float Distance(float x, float y, float cx, float cy)
        => MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

    private static Color Lerp(Color a, Color b, float t) => new(
        a.R + (b.R - a.R) * t,
        a.G + (b.G - a.G) * t,
        a.B + (b.B - a.B) * t,
        a.A + (b.A - a.A) * t);

    /// <summary>Hash inteiro → 0..1. Determinístico e independente de <c>Random</c>, então o
    /// mesmo estilo dá exatamente o mesmo PNG em qualquer máquina.</summary>
    private static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263) + (uint)seed * 2654435761u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFF_FFFF) / 16777216f;
    }

    private static void Put(byte[] pixels, int width, int x, int y, Color color, float alpha)
    {
        int i = (y * width + x) * 4;
        pixels[i + 0] = ToByte(color.R);
        pixels[i + 1] = ToByte(color.G);
        pixels[i + 2] = ToByte(color.B);
        pixels[i + 3] = ToByte(alpha);
    }

    private static byte ToByte(float value) => (byte)Math.Clamp(value * 255f + 0.5f, 0f, 255f);
}
