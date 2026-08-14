using Aurora.Runtime.Audio;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Preenchimento de buffer de streaming de música. A fonte real é o decodificador Vorbis, que
/// exige um arquivo ogg e um dispositivo OpenAL; aqui ela é falsa, então dá pra provar
/// exatamente os casos que quebrariam em produção: fim de faixa, loop, leitura parcial e
/// fonte vazia (o caso que faria o laço girar pra sempre).
/// </summary>
public class PcmFillerTests
{
    /// <summary>Fonte controlável: entrega os dados dados, conta rebobinadas e pode limitar
    /// quanto devolve por chamada — decodificador real devolve menos do que foi pedido quando
    /// esbarra em fronteira de pacote.</summary>
    private sealed class FonteFalsa : IPcmSource
    {
        private readonly float[] _dados;
        private int _posicao;

        public int RewindCount { get; private set; }
        public int MaxPorLeitura { get; set; } = int.MaxValue;

        public FonteFalsa(params float[] dados) => _dados = dados;

        public int Read(float[] buffer, int offset, int count)
        {
            int disponivel = Math.Min(Math.Min(count, MaxPorLeitura), _dados.Length - _posicao);
            if (disponivel <= 0)
                return 0;

            Array.Copy(_dados, _posicao, buffer, offset, disponivel);
            _posicao += disponivel;
            return disponivel;
        }

        public void Rewind()
        {
            RewindCount++;
            _posicao = 0;
        }
    }

    private static (int Total, short[] Pcm) Fill(FonteFalsa fonte, int tamanho, bool looping)
    {
        var floats = new float[tamanho];
        var pcm = new short[tamanho];
        int total = PcmFiller.Fill(fonte, floats, pcm, looping);
        return (total, pcm);
    }

    [Fact]
    public void EncheOBufferInteiroQuandoHaDadoDeSobra()
    {
        var fonte = new FonteFalsa(1f, 1f, 1f, 1f, 1f, 1f);

        var (total, _) = Fill(fonte, 4, looping: false);

        Assert.Equal(4, total);
        Assert.Equal(0, fonte.RewindCount);
    }

    [Fact]
    public void LeituraParcialEChamadaDeNovoAteEncher()
    {
        // Decodificador devolvendo 1 amostra por vez ainda tem que encher o buffer.
        var fonte = new FonteFalsa(0.5f, 0.5f, 0.5f, 0.5f) { MaxPorLeitura = 1 };

        var (total, _) = Fill(fonte, 4, looping: false);

        Assert.Equal(4, total);
    }

    [Fact]
    public void SemLoopOFimDaFaixaDevolveSoOQueVeio()
    {
        var fonte = new FonteFalsa(1f, 1f);

        var (total, _) = Fill(fonte, 8, looping: false);

        Assert.Equal(2, total);
        Assert.Equal(0, fonte.RewindCount);
    }

    [Fact]
    public void SemLoopFonteJaEsgotadaDevolveZero()
    {
        var fonte = new FonteFalsa();

        var (total, _) = Fill(fonte, 4, looping: false);

        Assert.Equal(0, total);
    }

    [Fact]
    public void ComLoopRebobinaEContinuaEnchendo()
    {
        var fonte = new FonteFalsa(1f, -1f);

        var floats = new float[6];
        var pcm = new short[6];
        int total = PcmFiller.Fill(fonte, floats, pcm, looping: true);

        Assert.Equal(6, total);
        Assert.Equal(2, fonte.RewindCount); // 2 amostras por volta, 3 voltas no total
        Assert.Equal(new short[] { 32767, -32767, 32767, -32767, 32767, -32767 }, pcm);
    }

    [Fact]
    public void ComLoopFonteVaziaDevolveZeroSemTravar()
    {
        // Este é o caso que faria o laço girar pra sempre sem a saída após a rebobinada.
        var fonte = new FonteFalsa();

        var (total, _) = Fill(fonte, 4, looping: true);

        Assert.Equal(0, total);
        Assert.Equal(1, fonte.RewindCount); // tentou rebobinar uma vez e desistiu
    }

    [Fact]
    public void NaoRebobinaQuandoOBufferJaEncheuExatamente()
    {
        // Fonte com exatamente o tamanho do buffer: encheu, então não há motivo pra tocar
        // no decodificador de novo neste refill.
        var fonte = new FonteFalsa(1f, 1f, 1f, 1f);

        var (total, _) = Fill(fonte, 4, looping: true);

        Assert.Equal(4, total);
        Assert.Equal(0, fonte.RewindCount);
    }

    [Fact]
    public void SoOPrefixoEscritoEAtualizadoNoPcm()
    {
        // O array é reusado entre refills e só as `total` primeiras amostras valem — é por isso
        // que o tamanho vai explícito pro OpenAL em vez de usar o array inteiro.
        var floats = new float[4];
        var pcm = new short[4];
        Array.Fill(pcm, (short)123);

        int total = PcmFiller.Fill(new FonteFalsa(1f, 1f), floats, pcm, looping: false);

        Assert.Equal(2, total);
        Assert.Equal(new short[] { 32767, 32767, 123, 123 }, pcm);
    }

    [Fact]
    public void BufferPcmMenorQueOFloatLancaErro()
    {
        Assert.Throws<ArgumentException>(
            () => PcmFiller.Fill(new FonteFalsa(1f), new float[8], new short[4], looping: false));
    }

    [Fact]
    public void FonteNulaLancaErro()
    {
        Assert.Throws<ArgumentNullException>(
            () => PcmFiller.Fill(null!, new float[4], new short[4], looping: false));
    }

    // ---- Conversão pra PCM 16 ----

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, 32767)]
    [InlineData(-1f, -32767)]
    [InlineData(0.5f, 16383)]
    public void ConversaoDeAmostraNormalizada(float amostra, short esperado)
    {
        Assert.Equal(esperado, PcmFiller.ToPcm16(amostra));
    }

    [Theory]
    [InlineData(2f, short.MaxValue)]
    [InlineData(-2f, short.MinValue)]
    [InlineData(1000f, short.MaxValue)]
    [InlineData(-1000f, short.MinValue)]
    public void AmostraForaDaFaixaSatura(float amostra, short esperado)
    {
        // Sem a saturação o cast daria wrap e um pico viraria estalo invertido.
        Assert.Equal(esperado, PcmFiller.ToPcm16(amostra));
    }
}
