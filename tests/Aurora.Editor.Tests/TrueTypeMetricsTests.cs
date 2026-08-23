using Aurora.Editor.Models;

namespace Aurora.Editor.Tests;

/// <summary>
/// Guarda a medição de texto do preview de UI. O editor mede UiText com estas métricas e o jogo
/// mede com <c>Aurora.Runtime.Graphics.Font</c> (stb_truetype) — as duas leem o MESMO .ttf e têm
/// que chegar no mesmo número, senão elemento ancorado em Center/Right aparece num lugar no
/// editor e em outro no jogo. Como <c>Font</c> só existe com contexto de GL, a checagem aqui é
/// contra os valores conhecidos do DejaVuSans e contra as invariantes das fórmulas.
/// </summary>
public sealed class TrueTypeMetricsTests
{
    // unitsPerEm 2048, ascender 1901, descender -483 → scale(22px) = 22 / 2384.
    private const float FontSize = 22f;

    private static TrueTypeMetrics Font()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf");
        var font = TrueTypeMetrics.FromFile(path);
        Assert.NotNull(font);
        return font;
    }

    private static void AssertClose(double expected, double actual, double tolerance = 0.05)
        => Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"esperado ~{expected}, veio {actual}");

    [Fact]
    public void ArquivoInexistente_DevolveNull()
        => Assert.Null(TrueTypeMetrics.FromFile(Path.Combine(Path.GetTempPath(), "nao-existe-mesmo.ttf")));

    [Fact]
    public void Advance_UsaAsMetricasDoArquivo()
    {
        var font = Font();

        // DejaVuSans: 'A' avança 1401 unidades, espaço 651, 'i' 569. Em 22px (scale 22/2384)
        // isso dá ~12.93, ~6.01 e ~5.25. Tolerância de 0.05px cobre arredondamento de float sem
        // deixar passar uma tabela lida errado (offset trocado erra por unidades inteiras).
        AssertClose(12.93, font.Advance('A', FontSize));
        AssertClose(6.01, font.Advance(' ', FontSize));
        AssertClose(5.25, font.Advance('i', FontSize));
    }

    [Fact]
    public void Advance_CaractereForaDoAtlas_CaiNoPontoDeInterrogacao()
    {
        var font = Font();

        // Font.GlyphIndex manda tudo fora de ASCII/Latin-1 pro '?'. Medir diferente disso faria a
        // caixa do preview divergir de um texto com emoji/CJK.
        Assert.Equal(font.Advance('?', FontSize), font.Advance('中', FontSize), 3);

        // Acentuado de Latin-1 é rasterizado de verdade — não pode virar '?'.
        Assert.NotEqual(font.Advance('?', FontSize), font.Advance('ã', FontSize), 3);
    }

    [Fact]
    public void Measure_LarguraEhASomaDosAvancos()
    {
        var font = Font();
        const string text = "Jogar";

        double expected = text.Sum(c => (double)font.Advance(c, FontSize));
        var (width, height) = font.Measure(text, FontSize, 1f);

        Assert.Equal(expected, width, 3);
        Assert.Equal(font.LineHeight(FontSize), height, 3);
    }

    [Fact]
    public void Measure_RespeitaEscalaEQuebraDeLinha()
    {
        var font = Font();

        var (width, height) = font.Measure("ab\ncd", FontSize, 1f);
        var (doubledWidth, doubledHeight) = font.Measure("ab\ncd", FontSize, 2f);

        Assert.Equal(2 * font.LineHeight(FontSize), height, 3);
        Assert.Equal(2 * width, doubledWidth, 3);
        Assert.Equal(2 * height, doubledHeight, 3);
    }

    [Fact]
    public void Measure_LarguraEhADaMaiorLinha()
    {
        var font = Font();

        var (wide, _) = font.Measure("wwwwww", FontSize, 1f);
        var (mixed, _) = font.Measure("i\nwwwwww\ni", FontSize, 1f);

        Assert.Equal(wide, mixed, 3);
    }

    [Fact]
    public void Wrap_QuebraNoLimiteEMantemTudoDentro()
    {
        var font = Font();
        const float maxWidth = 120f;

        string wrapped = font.Wrap("palavras suficientes para estourar a caixa", maxWidth, FontSize, 1f);

        Assert.Contains('\n', wrapped);
        foreach (string line in wrapped.Split('\n'))
            Assert.True(font.Measure(line, FontSize, 1f).Width <= maxWidth,
                $"linha maior que o limite: \"{line}\"");
    }

    [Fact]
    public void Wrap_SemLimite_NaoMexeNoTexto()
    {
        var font = Font();
        const string text = "sem quebra nenhuma aqui";

        Assert.Equal(text, font.Wrap(text, 0f, FontSize, 1f));
    }

    [Fact]
    public void Wrap_EscalaMaior_QuebraMaisCedo()
    {
        var font = Font();
        const string text = "uma frase de tamanho razoável para quebrar";

        int linhasEm1x = font.Wrap(text, 200f, FontSize, 1f).Count(c => c == '\n');
        int linhasEm2x = font.Wrap(text, 200f, FontSize, 2f).Count(c => c == '\n');

        // MaxWidth é pixel de tela: dobrar a escala do texto dobra o espaço consumido, então cabe
        // menos por linha. Se isso inverter, o preview quebra diferente do runtime.
        Assert.True(linhasEm2x > linhasEm1x, $"1x={linhasEm1x} linhas, 2x={linhasEm2x} linhas");
    }

    [Fact]
    public void LineHeight_EscalaLinearmenteComOTamanho()
    {
        var font = Font();

        // stbtt_ScaleForPixelHeight faz (ascender - descender) mapear exatamente pro tamanho
        // pedido, então LineHeight = pixelSize + lineGap*scale. DejaVuSans tem lineGap 0: a
        // altura de linha bate certinho com os 22px. Daí o >= em vez de >.
        Assert.True(font.LineHeight(FontSize) >= FontSize,
            $"LineHeight {font.LineHeight(FontSize)} menor que o tamanho de fonte {FontSize}");
        Assert.Equal(2 * font.LineHeight(FontSize), font.LineHeight(FontSize * 2), 3);
    }
}
