using System.Numerics;
using Silk.NET.OpenGL;
using StbTrueTypeSharp;

namespace Aurora.Runtime.Graphics;

/// <summary>
/// Fonte TTF rasterizada num atlas de glifos (ASCII 32–126 + Latin-1 160–255,
/// cobre português). Desenha via SpriteBatch — texto de uma fonte = 1 draw call.
/// </summary>
public sealed unsafe class Font : IDisposable
{
    private const int AtlasSize = 512;
    private const int AsciiFirst = 32;
    private const int AsciiCount = 95;   // 32..126
    private const int Latin1First = 160;
    private const int Latin1Count = 96;  // 160..255

    public Texture2D Atlas { get; }

    /// <summary>Tamanho em pixels usado no bake.</summary>
    public float Size { get; }

    /// <summary>Distância entre linhas (baseline a baseline).</summary>
    public float LineHeight { get; }

    /// <summary>Distância do topo do texto à baseline.</summary>
    public float Ascent { get; }

    private readonly StbTrueType.stbtt_packedchar[] _glyphs;

    private Font(Texture2D atlas, float size, float ascent, float lineHeight,
        StbTrueType.stbtt_packedchar[] glyphs)
    {
        Atlas = atlas;
        Size = size;
        Ascent = ascent;
        LineHeight = lineHeight;
        _glyphs = glyphs;
    }

    public static Font FromStream(GL gl, Stream ttf, float pixelSize)
    {
        using var buffer = new MemoryStream();
        ttf.CopyTo(buffer);
        return FromBytes(gl, buffer.ToArray(), pixelSize);
    }

    public static Font FromBytes(GL gl, byte[] ttf, float pixelSize)
    {
        var coverage = new byte[AtlasSize * AtlasSize];
        var glyphs = new StbTrueType.stbtt_packedchar[AsciiCount + Latin1Count];

        fixed (byte* pixels = coverage)
        fixed (byte* font = ttf)
        fixed (StbTrueType.stbtt_packedchar* chars = glyphs)
        {
            var context = new StbTrueType.stbtt_pack_context();
            if (StbTrueType.stbtt_PackBegin(context, pixels, AtlasSize, AtlasSize, AtlasSize, 1, null) == 0)
                throw new InvalidDataException("Falha ao iniciar o empacotamento da fonte.");

            StbTrueType.stbtt_PackSetOversampling(context, 2, 2);

            if (StbTrueType.stbtt_PackFontRange(context, font, 0, pixelSize, AsciiFirst, AsciiCount, chars) == 0
                || StbTrueType.stbtt_PackFontRange(context, font, 0, pixelSize, Latin1First, Latin1Count, chars + AsciiCount) == 0)
            {
                StbTrueType.stbtt_PackEnd(context);
                throw new InvalidDataException("Falha ao rasterizar a fonte (atlas pequeno demais?).");
            }

            StbTrueType.stbtt_PackEnd(context);
        }

        // Métricas verticais na escala do tamanho pedido.
        float ascent, descent, lineGap;
        fixed (byte* font = ttf)
        {
            var info = new StbTrueType.stbtt_fontinfo();
            if (StbTrueType.stbtt_InitFont(info, font, 0) == 0)
                throw new InvalidDataException("TTF inválido.");

            int rawAscent, rawDescent, rawLineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &rawAscent, &rawDescent, &rawLineGap);
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelSize);
            ascent = rawAscent * scale;
            descent = rawDescent * scale;
            lineGap = rawLineGap * scale;
        }

        // Cobertura vira RGBA branco com alpha — tinta com a cor no shader.
        var rgba = new byte[AtlasSize * AtlasSize * 4];
        for (int i = 0; i < coverage.Length; i++)
        {
            rgba[i * 4 + 0] = 255;
            rgba[i * 4 + 1] = 255;
            rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = coverage[i];
        }

        var atlas = Texture2D.FromPixels(gl, AtlasSize, AtlasSize, rgba);
        return new Font(atlas, pixelSize, ascent, ascent - descent + lineGap, glyphs);
    }

    private int GlyphIndex(char c) => c switch
    {
        >= (char)AsciiFirst and <= (char)(AsciiFirst + AsciiCount - 1) => c - AsciiFirst,
        >= (char)Latin1First and <= (char)(Latin1First + Latin1Count - 1) => AsciiCount + c - Latin1First,
        _ => '?' - AsciiFirst,
    };

    /// <summary>Desenha o texto com o canto superior esquerdo em <paramref name="position"/>.</summary>
    public void Draw(SpriteBatch batch, string text, Vector2 position, Color color, float scale = 1f)
    {
        float penX = 0f;
        float penY = Ascent;

        fixed (StbTrueType.stbtt_packedchar* chars = _glyphs)
        {
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    penX = 0f;
                    penY += LineHeight;
                    continue;
                }

                var quad = new StbTrueType.stbtt_aligned_quad();
                float x = penX, y = penY;
                StbTrueType.stbtt_GetPackedQuad(chars, AtlasSize, AtlasSize, GlyphIndex(c),
                    &x, &y, &quad, 0);
                penX = x;

                var destination = position + new Vector2(quad.x0, quad.y0) * scale;
                var size = new Vector2(quad.x1 - quad.x0, quad.y1 - quad.y0) * scale;
                var source = new RectF(quad.s0 * AtlasSize, quad.t0 * AtlasSize,
                    (quad.s1 - quad.s0) * AtlasSize, (quad.t1 - quad.t0) * AtlasSize);

                batch.Draw(Atlas, destination, size, Vector2.Zero, 0f, color, source);
            }
        }
    }

    /// <summary>Largura e altura do texto (respeita '\n').</summary>
    public Vector2 MeasureText(string text, float scale = 1f)
    {
        float maxWidth = 0f;
        float penX = 0f;
        int lines = 1;

        fixed (StbTrueType.stbtt_packedchar* chars = _glyphs)
        {
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    maxWidth = MathF.Max(maxWidth, penX);
                    penX = 0f;
                    lines++;
                    continue;
                }

                penX += chars[GlyphIndex(c)].xadvance;
            }
        }

        maxWidth = MathF.Max(maxWidth, penX);
        return new Vector2(maxWidth * scale, lines * LineHeight * scale);
    }

    public void Dispose() => Atlas.Dispose();
}
