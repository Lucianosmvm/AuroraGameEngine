using Bichinhos;

namespace BichinhosTests;

/// <summary>
/// Regras do bichinho sem janela: relógio das necessidades, XP e evolução, batalha e save. Usa o
/// especies.json de verdade (copiado pro bin junto com o jogo) — se alguém quebrar o JSON, cai aqui.
/// </summary>
public class BichinhosTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "Assets");

    public BichinhosTests()
        => Catalogo.Atual = Catalogo.Carregar(File.ReadAllText(Path.Combine(Assets, Catalogo.Caminho)));

    private static Bicho Novo(string especie = "brasinha") => new() { EspecieId = especie };

    // ------------------------------------------------------------------ catálogo

    [Fact]
    public void Catalogo_TodoGolpeCitadoExisteETodoSpriteExiste()
    {
        foreach (var especie in Catalogo.Atual.Especies)
        {
            Assert.Equal(3, especie.Estagios.Count);
            foreach (var g in especie.Golpes)
                Catalogo.Atual.Golpe(g.Golpe);   // joga se não existir
            for (int e = 0; e < 3; e++)
                Assert.True(File.Exists(Path.Combine(Assets, especie.Sprite(e))), especie.Sprite(e));
            Assert.Contains(especie.Golpes, g => g.Nivel == 1);
        }

        Assert.Equal(3, Catalogo.Atual.Iniciais.Count());
    }

    [Theory]
    [InlineData(Tipo.Fogo, Tipo.Planta, 2f)]
    [InlineData(Tipo.Planta, Tipo.Agua, 2f)]
    [InlineData(Tipo.Agua, Tipo.Fogo, 2f)]
    [InlineData(Tipo.Planta, Tipo.Fogo, 0.5f)]
    [InlineData(Tipo.Fogo, Tipo.Fogo, 0.5f)]
    [InlineData(Tipo.Normal, Tipo.Fogo, 1f)]
    [InlineData(Tipo.Agua, Tipo.Normal, 1f)]
    public void Efetividade_SegueOTriangulo(Tipo ataque, Tipo defensor, float esperado)
        => Assert.Equal(esperado, Tipos.Efetividade(ataque, defensor));

    [Fact]
    public void GolpesNoNivel_UsaOsQuatroUltimosAprendidos()
    {
        var especie = Catalogo.Atual.Especie("brasinha");
        Assert.Single(Catalogo.Atual.GolpesNoNivel(especie, 1));
        var nv20 = Catalogo.Atual.GolpesNoNivel(especie, 20);
        Assert.Equal(4, nv20.Count);
        Assert.DoesNotContain(nv20, g => g.Id == "arranhao");
        Assert.Contains(nv20, g => g.Id == "inferno");
    }

    // ------------------------------------------------------------------ relógio

    [Fact]
    public void Avancar_UmaHoraAcordado_DaFomeECansa()
    {
        var b = Novo();
        b.Avancar(1.0, new Random(1));
        Assert.InRange(b.Fome, 60f, 75f);
        Assert.True(b.Energia < 100f);
        Assert.False(b.Dormindo);
    }

    [Fact]
    public void Avancar_DuasHoras_FazCoco()
    {
        var b = Novo();
        b.Avancar(2.0, new Random(1));
        Assert.True(b.Cocos >= 1);
    }

    [Fact]
    public void Dormindo_RecuperaEnergiaEAcordaSozinho()
    {
        var b = Novo();
        b.Energia = 10f;
        b.Dormindo = true;
        b.Avancar(3.0, new Random(1));
        Assert.False(b.Dormindo);
        Assert.True(b.Energia > 90f);
    }

    [Fact]
    public void UmDiaAbandonado_AdoeceEPassaFome()
    {
        var b = Novo();
        b.Avancar(Progresso.MaxHorasFora, new Random(3));
        Assert.True(b.Doente);
        Assert.True(b.Saude < 50f);
        Assert.Equal(0f, b.Fome);
    }

    [Fact]
    public void Comer_RecusaQuandoCheio()
    {
        var b = Novo();
        b.Fome = 96f;
        Assert.False(b.Comer(out _));
        b.Fome = 40f;
        Assert.True(b.Comer(out _));
        Assert.Equal(75f, b.Fome);
    }

    [Fact]
    public void Remedio_CuraDoenca()
    {
        var b = Novo();
        b.Doente = true;
        b.Saude = 30f;
        Assert.True(b.TomarRemedio(out _));
        Assert.False(b.Doente);
        Assert.Equal(65f, b.Saude);
    }

    // ------------------------------------------------------------------ XP e evolução

    [Fact]
    public void GanharXp_SobeVariosNiveisEAnunciaGolpe()
    {
        var b = Novo();
        var (niveis, golpes) = b.GanharXp(Bicho.XpNecessario(1) + Bicho.XpNecessario(2) + 1);
        Assert.Equal([2, 3], niveis);
        Assert.Equal(3, b.Nivel);
        Assert.Equal(1, b.Xp);
        Assert.Contains(golpes, g => g.Id == "brasa");
    }

    [Fact]
    public void Evolucao_FicaPendenteNoLimiarEAvancaUmEstagioPorVez()
    {
        var b = Novo();
        b.Nivel = 5;
        Assert.False(b.EvolucaoPendente);

        b.Nivel = 14;
        Assert.True(b.EvolucaoPendente);
        b.Evoluir();
        Assert.Equal(1, b.Estagio);
        Assert.True(b.EvolucaoPendente);
        b.Evoluir();
        Assert.Equal(2, b.Estagio);
        Assert.False(b.EvolucaoPendente);
        Assert.Equal("Vulcanossauro", b.Nome);
    }

    [Fact]
    public void Evolucao_AumentaOsAtributos()
    {
        var especie = Catalogo.Atual.Especie("gotinha");
        var antes = Catalogo.Atual.AtributosNoNivel(especie, 14, 1);
        var depois = Catalogo.Atual.AtributosNoNivel(especie, 14, 2);
        Assert.True(depois.Ataque > antes.Ataque);
        Assert.True(depois.Vida > antes.Vida);
    }

    [Fact]
    public void NivelMaximo_NaoPassa()
    {
        var b = Novo();
        b.Nivel = Catalogo.Atual.NivelMaximo;
        var (niveis, _) = b.GanharXp(100_000);
        Assert.Empty(niveis);
        Assert.Equal(Catalogo.Atual.NivelMaximo, b.Nivel);
    }

    // ------------------------------------------------------------------ batalha

    private static Lutador Selvagem(string especie, int nivel) => Lutador.Selvagem(Catalogo.Atual.Especie(especie), nivel);

    [Fact]
    public void Dano_SuperEficazBateMaisQueNeutro()
    {
        var fogo = Selvagem("brasinha", 10);
        var planta = Selvagem("brotinho", 10);
        var normal = Selvagem("pelucinho", 10);
        var brasa = Catalogo.Atual.Golpe("brasa");

        int contraPlanta = Batalha.CalcularDano(fogo, planta, brasa, Tipos.Efetividade(brasa.Tipo, planta.Tipo), 1f);
        int contraNormal = Batalha.CalcularDano(fogo, normal, brasa, Tipos.Efetividade(brasa.Tipo, normal.Tipo), 1f);
        Assert.True(contraPlanta > contraNormal * 1.5f);
    }

    [Fact]
    public void Turno_MaisRapidoAtacaPrimeiro()
    {
        var rapido = Selvagem("pipio", 10);      // velocidade base 66
        var lento = Selvagem("pelucinho", 10);   // velocidade base 40
        var batalha = new Batalha(rapido, lento, Dificuldade.Normal, new Random(5));

        var eventos = batalha.Turno(rapido.Golpes[0]);
        var primeira = Assert.IsType<Mensagem>(eventos[0]);
        Assert.StartsWith(rapido.Nome, primeira.Texto);
    }

    [Fact]
    public void Batalha_SempreTermina()
    {
        for (int semente = 0; semente < 30; semente++)
        {
            var rng = new Random(semente);
            var jogador = Selvagem("gotinha", 8);
            var inimigo = Batalha.SortearInimigo(8, Dificuldade.Normal, rng);
            var batalha = new Batalha(jogador, inimigo, Dificuldade.Normal, rng);

            int turnos = 0;
            while (!batalha.Acabou && turnos++ < 200)
                batalha.Turno(jogador.Golpes.First(g => g.Poder > 0));

            Assert.True(batalha.Acabou, $"semente {semente} não acabou");
        }
    }

    [Fact]
    public void Recompensa_DificilPagaMais()
    {
        var inimigo = Selvagem("pipio", 10);
        var facil = new Batalha(Selvagem("brasinha", 10), inimigo, Dificuldade.Facil, new Random(1)).Recompensa();
        var dificil = new Batalha(Selvagem("brasinha", 10), inimigo, Dificuldade.Dificil, new Random(1)).Recompensa();
        Assert.True(dificil.Xp > facil.Xp);
        Assert.True(dificil.Moedas > facil.Moedas);
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public void Progresso_SalvaECarrega()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "bichinhos-teste-" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = new Progresso { Moedas = 55, Bicho = new Bicho { EspecieId = "brotinho", Nivel = 7, Estagio = 1, Cocos = 2, Doente = true } };
            p.Salvar(pasta);

            var lido = Progresso.Carregar(pasta);
            Assert.Equal(55, lido.Moedas);
            Assert.Equal("brotinho", lido.Bicho!.EspecieId);
            Assert.Equal(7, lido.Bicho.Nivel);
            Assert.Equal(2, lido.Bicho.Cocos);
            Assert.True(lido.Bicho.Doente);
            Assert.InRange(lido.HorasDesdeUltimaVez(DateTime.UtcNow.AddHours(30)), 23.9, 24.0);
        }
        finally
        {
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Progresso_SaveCorrompidoComecaDoZero()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "bichinhos-teste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, Progresso.Arquivo), "{ isso não é json");
            Assert.Null(Progresso.Carregar(pasta).Bicho);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }
}
