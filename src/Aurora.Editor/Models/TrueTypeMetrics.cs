using System.Buffers.Binary;

namespace Aurora.Editor.Models;

/// <summary>
/// Métricas de um .ttf lidas direto do arquivo (tabelas <c>hhea</c>, <c>hmtx</c>, <c>cmap</c>),
/// sem precisar de contexto gráfico.
///
/// <para>Existe pra o editor medir texto do jeito EXATO que <c>Aurora.Runtime.Graphics.Font</c>
/// mede em runtime. O editor não referencia o runtime de propósito (ele trabalha só em cima do
/// JSON da cena), e a <c>Font</c> só existe com GL vivo — o atlas de glifos mora na GPU. Sem isso
/// o editor caía no chute "largura ≈ 7px por caractere", e como o tamanho do texto entra no
/// cálculo de AnchorX/AnchorY, um UiText ancorado em Center/Right aparecia no lugar errado no
/// preview.</para>
///
/// <para>As fórmulas espelham o que a <c>Font</c> faz via stb_truetype:
/// <c>scale = pixelSize / (ascender - descender)</c> (stbtt_ScaleForPixelHeight),
/// <c>LineHeight = (ascender - descender + lineGap) * scale</c>, e o avanço de cada glifo é o
/// valor de <c>hmtx</c> vezes esse mesmo scale.</para>
/// </summary>
public sealed class TrueTypeMetrics
{
    // Mesma cobertura do atlas de Font: ASCII 32–126 e Latin-1 160–255. Fora disso a Font desenha
    // '?', então medir qualquer outra coisa como '?' mantém o preview alinhado com o jogo.
    private const char AsciiFirst = (char)32;
    private const char AsciiLast = (char)126;
    private const char Latin1First = (char)160;
    private const char Latin1Last = (char)255;

    private readonly ushort[] _advanceUnits;
    private readonly Dictionary<char, ushort> _glyphOfChar;
    private readonly int _ascender;
    private readonly int _descender;
    private readonly int _lineGap;

    private TrueTypeMetrics(ushort[] advanceUnits, Dictionary<char, ushort> glyphOfChar,
        int ascender, int descender, int lineGap)
    {
        _advanceUnits = advanceUnits;
        _glyphOfChar = glyphOfChar;
        _ascender = ascender;
        _descender = descender;
        _lineGap = lineGap;
    }

    /// <summary>Lê o arquivo. Devolve null se não existir ou não for um TTF que dê pra ler —
    /// o chamador cai num fallback, preview aproximado é melhor que editor que não abre.</summary>
    public static TrueTypeMetrics? FromFile(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                    or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Fator de escala de unidades de fonte pra pixels, igual stbtt_ScaleForPixelHeight.</summary>
    private float ScaleFor(float pixelSize)
    {
        int span = _ascender - _descender;
        return span > 0 ? pixelSize / span : 0f;
    }

    /// <summary>Distância baseline-a-baseline em pixels — espelha <c>Font.LineHeight</c>.</summary>
    public float LineHeight(float pixelSize)
        => (_ascender - _descender + _lineGap) * ScaleFor(pixelSize);

    /// <summary>Avanço horizontal do caractere em pixels — espelha <c>Font.Advance</c>.</summary>
    public float Advance(char c, float pixelSize)
    {
        char mapped = c is >= AsciiFirst and <= AsciiLast or >= Latin1First and <= Latin1Last ? c : '?';
        ushort glyph = _glyphOfChar.TryGetValue(mapped, out ushort id) ? id : (ushort)0;
        ushort units = glyph < _advanceUnits.Length ? _advanceUnits[glyph] : _advanceUnits[^1];
        return units * ScaleFor(pixelSize);
    }

    /// <summary>Largura e altura do texto, respeitando <c>'\n'</c> — espelha <c>Font.MeasureText</c>.</summary>
    public (double Width, double Height) Measure(string text, float pixelSize, float scale)
    {
        double maxWidth = 0, penX = 0;
        int lines = 1;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                maxWidth = Math.Max(maxWidth, penX);
                penX = 0;
                lines++;
                continue;
            }
            penX += Advance(c, pixelSize);
        }

        maxWidth = Math.Max(maxWidth, penX);
        return (maxWidth * scale, lines * LineHeight(pixelSize) * scale);
    }

    /// <summary>Insere <c>'\n'</c> pra caber em <paramref name="maxWidth"/> pixels de tela —
    /// espelha <c>Font.WrapText</c>, inclusive reusando o mesmo <c>TextWrapper</c> do runtime
    /// (o arquivo entra aqui por link no .csproj, não por cópia).</summary>
    public string Wrap(string text, float maxWidth, float pixelSize, float scale)
    {
        if (maxWidth <= 0f)
            return text;

        float limit = scale > 0f ? maxWidth / scale : maxWidth;
        return Aurora.Runtime.Graphics.TextWrapper.Wrap(text, limit, c => Advance(c, pixelSize));
    }

    // ---- Parsing ----

    private static TrueTypeMetrics Parse(byte[] data)
    {
        int numTables = ReadU16(data, 4);
        var tables = new Dictionary<string, int>(numTables, StringComparer.Ordinal);

        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(data, record, 4);
            tables[tag] = (int)ReadU32(data, record + 8);
        }

        if (!tables.TryGetValue("hhea", out int hhea) || !tables.TryGetValue("hmtx", out int hmtx))
            throw new InvalidDataException("TTF sem hhea/hmtx.");

        int ascender = ReadI16(data, hhea + 4);
        int descender = ReadI16(data, hhea + 6);
        int lineGap = ReadI16(data, hhea + 8);
        int numberOfHMetrics = ReadU16(data, hhea + 34);

        if (numberOfHMetrics <= 0)
            throw new InvalidDataException("TTF sem métricas horizontais.");

        // Glifos além de numberOfHMetrics reusam o último avanço (regra do formato) — guardar só
        // os longHorMetric basta, Advance faz o clamp.
        var advances = new ushort[numberOfHMetrics];
        for (int i = 0; i < numberOfHMetrics; i++)
            advances[i] = ReadU16(data, hmtx + i * 4);

        var glyphOfChar = tables.TryGetValue("cmap", out int cmap)
            ? ReadCmap(data, cmap)
            : [];

        return new TrueTypeMetrics(advances, glyphOfChar, ascender, descender, lineGap);
    }

    /// <summary>Mapa caractere→glifo. Só o formato 4 (BMP) interessa: a Font do runtime nem
    /// rasteriza nada fora de Latin-1.</summary>
    private static Dictionary<char, ushort> ReadCmap(byte[] data, int cmap)
    {
        int numTables = ReadU16(data, cmap + 2);
        int best = -1;

        for (int i = 0; i < numTables; i++)
        {
            int record = cmap + 4 + i * 8;
            int platform = ReadU16(data, record);
            int encoding = ReadU16(data, record + 2);
            int subtable = cmap + (int)ReadU32(data, record + 4);

            if (ReadU16(data, subtable) != 4)
                continue;

            // Windows/Unicode BMP é a escolha preferida; Unicode puro serve de reserva.
            bool preferred = platform == 3 && encoding == 1;
            if (preferred)
                return ReadCmapFormat4(data, subtable);
            if (best < 0 && (platform == 0 || platform == 3))
                best = subtable;
        }

        return best >= 0 ? ReadCmapFormat4(data, best) : [];
    }

    private static Dictionary<char, ushort> ReadCmapFormat4(byte[] data, int subtable)
    {
        int segCount = ReadU16(data, subtable + 6) / 2;
        int endCodes = subtable + 14;
        int startCodes = endCodes + segCount * 2 + 2;   // +2 pelo reservedPad
        int idDeltas = startCodes + segCount * 2;
        int idRangeOffsets = idDeltas + segCount * 2;

        var map = new Dictionary<char, ushort>(256);

        for (int seg = 0; seg < segCount; seg++)
        {
            int end = ReadU16(data, endCodes + seg * 2);
            int start = ReadU16(data, startCodes + seg * 2);
            int delta = ReadI16(data, idDeltas + seg * 2);
            int rangeOffset = ReadU16(data, idRangeOffsets + seg * 2);

            if (start > end || start == 0xFFFF)
                continue;

            // Só os intervalos que a Font realmente rasteriza — varrer o BMP inteiro seria
            // dezenas de milhares de entradas jogadas fora.
            int from = Math.Max(start, AsciiFirst);
            int to = Math.Min(end, Latin1Last);

            for (int code = from; code <= to; code++)
            {
                ushort glyph;
                if (rangeOffset == 0)
                {
                    glyph = (ushort)((code + delta) & 0xFFFF);
                }
                else
                {
                    int at = idRangeOffsets + seg * 2 + rangeOffset + (code - start) * 2;
                    if (at + 1 >= data.Length)
                        continue;
                    ushort raw = ReadU16(data, at);
                    if (raw == 0)
                        continue;
                    glyph = (ushort)((raw + delta) & 0xFFFF);
                }

                if (glyph != 0)
                    map[(char)code] = glyph;
            }
        }

        return map;
    }

    private static ushort ReadU16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));

    private static short ReadI16(byte[] data, int offset)
        => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));

    private static uint ReadU32(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
}
