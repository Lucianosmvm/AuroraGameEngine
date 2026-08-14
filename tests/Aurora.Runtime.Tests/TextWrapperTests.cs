using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Quebra de linha por largura. Usa uma fonte falsa de largura fixa (10 por caractere) pra
/// que a largura em pixels seja simplesmente <c>10 × número de caracteres</c> — assim cada
/// caso diz exatamente onde a quebra deve cair, sem depender de métricas de TTF real.
/// </summary>
public class TextWrapperTests
{
    private const float CharWidth = 10f;

    private static readonly Func<char, float> Monospace = _ => CharWidth;

    /// <summary>Largura que comporta exatamente <paramref name="chars"/> caracteres.</summary>
    private static float Largura(int chars) => chars * CharWidth;

    private static string Wrap(string text, int larguraEmChars)
        => TextWrapper.Wrap(text, Largura(larguraEmChars), Monospace);

    [Fact]
    public void TextoQueJaCabeNaoEAlterado()
    {
        Assert.Equal("abc def", Wrap("abc def", 10));
    }

    [Fact]
    public void TextoNoLimiteExatoNaoQuebra()
    {
        Assert.Equal("abcde", Wrap("abcde", 5));
    }

    [Fact]
    public void QuebraNoUltimoEspacoQueCabe()
    {
        // "abc def" tem 7 chars; com 5 de largura, "abc" cabe e "def" desce.
        Assert.Equal("abc\ndef", Wrap("abc def", 5));
    }

    [Fact]
    public void EspacoDaQuebraNaoVaiParaAProximaLinha()
    {
        string resultado = Wrap("abc def", 5);

        Assert.DoesNotContain(" ", resultado);
        Assert.Equal(["abc", "def"], resultado.Split('\n'));
    }

    [Fact]
    public void VariosEspacosNoPontoDeQuebraSaoConsumidos()
    {
        Assert.Equal(["abc", "def"], Wrap("abc     def", 5).Split('\n'));
    }

    [Fact]
    public void QuebraEmVariasLinhas()
    {
        Assert.Equal(["aa", "bb", "cc", "dd"], Wrap("aa bb cc dd", 2).Split('\n'));
    }

    [Fact]
    public void EncaixaOMaximoDePalavrasPorLinha()
    {
        // Largura 8: "aa bb" = 5 cabe, "aa bb cc" = 8 cabe, "aa bb cc d" não.
        Assert.Equal(["aa bb cc", "dd ee"], Wrap("aa bb cc dd ee", 8).Split('\n'));
    }

    [Fact]
    public void PalavraMaiorQueALinhaEQuebradaNoMeio()
    {
        // Sem espaço utilizável, cortar é melhor que vazar da caixa.
        Assert.Equal(["abcd", "efgh", "ij"], Wrap("abcdefghij", 4).Split('\n'));
    }

    [Fact]
    public void PalavraGiganteDepoisDeUmaCurtaComecaEmLinhaNova()
    {
        Assert.Equal(["ab", "cdef", "gh"], Wrap("ab cdefgh", 4).Split('\n'));
    }

    [Fact]
    public void QuebrasExistentesSaoPreservadas()
    {
        Assert.Equal(["ab", "cd"], Wrap("ab\ncd", 10).Split('\n'));
    }

    [Fact]
    public void QuebraExistenteReiniciaAContagemDeLargura()
    {
        // Cada parágrafo é medido do zero — "abc" não herda a largura gasta na linha anterior.
        Assert.Equal(["abc", "abc"], Wrap("abc\nabc", 3).Split('\n'));
    }

    [Fact]
    public void QuebraAutomaticaConviveComQuebraManual()
    {
        Assert.Equal(["aa", "bb", "cc"], Wrap("aa bb\ncc", 2).Split('\n'));
    }

    [Fact]
    public void LinhaVaziaEntreParagrafosSobrevive()
    {
        Assert.Equal(["ab", "", "cd"], Wrap("ab\n\ncd", 10).Split('\n'));
    }

    [Fact]
    public void NaoSobraLinhaVaziaNoFimQuandoSoRestamEspacos()
    {
        Assert.Equal("abc", Wrap("abc   ", 3));
    }

    [Fact]
    public void LarguraZeroOuNegativaDesligaAQuebra()
    {
        Assert.Equal("aa bb cc", TextWrapper.Wrap("aa bb cc", 0f, Monospace));
        Assert.Equal("aa bb cc", TextWrapper.Wrap("aa bb cc", -5f, Monospace));
    }

    [Fact]
    public void LarguraMenorQueUmCaractereNaoTravaEPoeUmPorLinha()
    {
        // O primeiro caractere da linha sempre entra; sem essa exceção o laço não avançaria.
        Assert.Equal(["a", "b", "c"], TextWrapper.Wrap("abc", 1f, Monospace).Split('\n'));
    }

    [Fact]
    public void TextoVazioVoltaVazio()
    {
        Assert.Equal("", Wrap("", 5));
    }

    [Fact]
    public void UmCaractereSoNuncaQuebra()
    {
        Assert.Equal("a", Wrap("a", 1));
    }

    [Fact]
    public void LarguraDeCaractereVariavelERespeitada()
    {
        // 'm' custa o dobro dos demais. Mesmo número de caracteres, resultado diferente:
        // "ii ii" mede 50 e cabe inteiro; "mm ii" mede 70 e precisa quebrar.
        Func<char, float> larguras = c => c == 'm' ? 20f : 10f;

        Assert.Equal("ii ii", TextWrapper.Wrap("ii ii", 50f, larguras));
        Assert.Equal(["mm", "ii"], TextWrapper.Wrap("mm ii", 50f, larguras).Split('\n'));
    }

    [Fact]
    public void NenhumaLinhaPassaDaLarguraQuandoHaOndeQuebrar()
    {
        const string texto = "O rato roeu a roupa do rei de Roma numa tarde bem quente de verao";
        const int limite = 12;

        foreach (string linha in Wrap(texto, limite).Split('\n'))
            Assert.True(linha.Length <= limite, $"Linha estourou o limite: '{linha}' ({linha.Length} chars)");
    }

    [Fact]
    public void QuebraPreservaTodosOsCaracteresNaoBrancos()
    {
        const string texto = "O rato roeu a roupa do rei de Roma numa tarde bem quente de verao";

        string semBrancos = new(texto.Where(c => c != ' ').ToArray());
        string resultado = new(Wrap(texto, 12).Where(c => c != ' ' && c != '\n').ToArray());

        Assert.Equal(semBrancos, resultado);
    }

    [Fact]
    public void AdvanceNuloLancaErro()
    {
        Assert.Throws<ArgumentNullException>(() => TextWrapper.Wrap("abc", 10f, null!));
    }
}
