using Bichinhos;

namespace BichinhosTests;

/// <summary>Quando avisar "ele está com fome" com o jogo fechado. Regra pura, sem Android.</summary>
public class LembretesTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "Assets");

    public LembretesTests()
        => Catalogo.Atual = Catalogo.Carregar(File.ReadAllText(Path.Combine(Assets, Catalogo.Caminho)));

    private static Bicho Bicho(float fome, bool dormindo = false, float energia = 100f)
        => new() { EspecieId = "brasinha", Apelido = "Fumaça", Fome = fome, Dormindo = dormindo, Energia = energia };

    private static DateTime Hora(int h, int m = 0) => new(2026, 3, 10, h, m, 0, DateTimeKind.Local);

    // ------------------------------------------------------------------ horas até a fome

    [Fact]
    public void HorasAteFome_AcordadoGastaDozePorHora()
        => Assert.Equal(50.0 / 12.0, Bicho(80f).HorasAteFome(30f), 6);

    [Fact]
    public void HorasAteFome_JaComFomeEZero()
    {
        Assert.Equal(0.0, Bicho(30f).HorasAteFome(30f));
        Assert.Equal(0.0, Bicho(10f).HorasAteFome(30f));
    }

    [Fact]
    public void HorasAteFome_DormindoGastaMaisDevagarEDepoisVoltaAoNormal()
    {
        // Energia 20: dorme (100-20)/45 h gastando 35% da fome; depois acordado, 12/h.
        var b = Bicho(80f, dormindo: true, energia: 20f);
        double sono = 80.0 / 45.0;
        double fomeAoAcordar = 80.0 - 12.0 * 0.35 * sono;
        Assert.Equal(sono + (fomeAoAcordar - 30.0) / 12.0, b.HorasAteFome(30f), 6);
    }

    [Fact]
    public void HorasAteFome_ChegaAoLimiteAindaDormindo()
    {
        // Dormiu faminto e o sono é longo: a fome cruza o limite antes de acordar.
        var b = Bicho(35f, dormindo: true, energia: 0f);
        Assert.Equal(5.0 / (12.0 * 0.35), b.HorasAteFome(30f), 6);
    }

    [Fact]
    public void HorasAteFome_BateComASimulacaoDeVerdade()
    {
        foreach (var (fome, dormindo, energia) in new[] { (90f, false, 100f), (60f, true, 30f), (45f, true, 90f) })
        {
            var previsto = Bicho(fome, dormindo, energia);
            double horas = previsto.HorasAteFome(30f);

            var real = Bicho(fome, dormindo, energia);
            real.Avancar(horas - 0.02, new Random(1));
            Assert.True(real.Fome > 30f, $"fome {fome}: ainda não devia ter cruzado ({real.Fome})");
            real.Avancar(0.04, new Random(1));
            Assert.True(real.Fome <= 30f, $"fome {fome}: devia ter cruzado ({real.Fome})");
        }
    }

    // ------------------------------------------------------------------ quando avisar

    [Fact]
    public void QuandoAvisar_UsaOMomentoEmQueACaiAoLimite()
    {
        var quando = Lembretes.QuandoAvisarFome(Bicho(66f), Hora(9));   // 36/12 = 3 h
        Assert.Equal(Hora(12), quando);
    }

    [Fact]
    public void QuandoAvisar_JaComFomeLembraUmaHoraDepois()
        => Assert.Equal(Hora(15), Lembretes.QuandoAvisarFome(Bicho(10f), Hora(14)));

    [Fact]
    public void QuandoAvisar_NuncaAvisaEmMenosDeDezMinutos()
    {
        var quando = Lembretes.QuandoAvisarFome(Bicho(30.5f), Hora(10))!.Value;   // cruza em 2 min
        Assert.Equal(Hora(10, 10), quando);
    }

    [Fact]
    public void QuandoAvisar_DeMadrugadaVaiParaAsOito()
    {
        // 20h + 4 h = 0h: cai no silêncio e vira 8h do mesmo dia.
        var quando = Lembretes.QuandoAvisarFome(Bicho(78f), Hora(20))!.Value;
        Assert.Equal(new DateTime(2026, 3, 11, 8, 0, 0), quando);
    }

    [Theory]
    [InlineData(7, 59, 8, 0, 10)]     // antes das 8h -> 8h do mesmo dia
    [InlineData(8, 0, 8, 0, 10)]      // 8h em ponto fica
    [InlineData(21, 59, 21, 59, 10)]  // ainda é hora de avisar
    [InlineData(22, 0, 8, 0, 11)]     // 22h em ponto já é silêncio -> 8h do dia seguinte
    [InlineData(23, 30, 8, 0, 11)]
    [InlineData(0, 15, 8, 0, 10)]
    public void Silencio_AjustaMadrugada(int h, int m, int horaEsperada, int minutoEsperado, int diaEsperado)
    {
        var ajustado = Lembretes.AjustarParaHorarioSilencioso(new DateTime(2026, 3, 10, h, m, 0));
        Assert.Equal(new DateTime(2026, 3, diaEsperado, horaEsperada, minutoEsperado, 0), ajustado);
    }

    [Fact]
    public void Texto_UsaOApelidoDoBicho()
    {
        var (titulo, texto) = Lembretes.TextoFome(Bicho(20f));
        Assert.Equal("Fumaça está com fome!", titulo);
        Assert.False(string.IsNullOrWhiteSpace(texto));
    }

    [Fact]
    public void Progresso_ParaNovoBichoMantemQueJaPediuPermissao()
    {
        var novo = new Progresso { PediuNotificacoes = true }.ParaNovoBicho(null);
        Assert.True(novo.PediuNotificacoes);
    }
}
