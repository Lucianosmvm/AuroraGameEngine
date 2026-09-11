using System.Numerics;
using BeastArena.Sim;

namespace BeastArenaTests;

public class BatalhaTests
{
    // ------------------------------------------------------------------ montagem

    /// <summary>Carta sintética: os testes de regra não podem quebrar porque alguém rebalanceou
    /// o cartas.json.</summary>
    private static CartaDef Carta(string id, Action<CartaDef>? ajustar = null)
    {
        var carta = new CartaDef { Id = id, Nome = id, Custo = 1, Vida = 100f, Dano = 10f };
        ajustar?.Invoke(carta);
        return carta;
    }

    /// <summary>Não anda e não machuca: fica exatamente onde foi posta, pra testar captura sem
    /// briga nem caminho no meio.</summary>
    private static CartaDef Estatua(string id, Action<CartaDef>? ajustar = null) => Carta(id, c =>
    {
        c.Vida = 100_000f;
        c.Dano = 0f;
        c.Velocidade = 0f;
        ajustar?.Invoke(c);
    });

    private static List<CartaDef> Baralho(params CartaDef[] primeiras)
    {
        var baralho = primeiras.ToList();
        while (baralho.Count < CatalogoCartas.TamanhoDoBaralho)
            baralho.Add(Carta($"enchimento{baralho.Count}"));
        return baralho;
    }

    private static Batalha Nova(List<CartaDef>? jogador = null, List<CartaDef>? inimigo = null)
        => new(jogador ?? Baralho(), inimigo ?? Baralho(), semente: 7);

    private static List<CartaDef> SoCom(CartaDef carta) => Enumerable.Repeat(carta, CatalogoCartas.TamanhoDoBaralho).ToList();

    private static void Rodar(Batalha batalha, float segundos, Action? aCadaPasso = null)
    {
        int passos = (int)MathF.Ceiling(segundos / Batalha.Passo);
        for (int i = 0; i < passos; i++)
        {
            batalha.Avancar();
            aCadaPasso?.Invoke();
        }
    }

    private static void Dominar(Santuario santuario, Equipe equipe)
    {
        santuario.Influencia = equipe == Equipe.Jogador ? 1f : -1f;
        santuario.Dono = equipe;
    }

    private static CatalogoCartas CatalogoReal()
        => CatalogoCartas.Carregar(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "cartas.json")));

    private static readonly Vector2 PertoDoNinho = new(9f, 16f);

    // ------------------------------------------------------------------ catálogo

    [Fact]
    public void CatalogoDoJogo_Carrega_EFechaUmBaralhoDeOito()
    {
        var catalogo = CatalogoReal();

        Assert.Equal(CatalogoCartas.TamanhoDoBaralho, catalogo.Baralho().Count);
        Assert.Contains(catalogo.Todas, c => c.Evolucoes.Count > 0);
    }

    [Fact]
    public void Catalogo_EvolucaoComXpForaDeOrdem_Recusa()
    {
        const string json = """
            {
              "Baralho": ["a","a","a","a","a","a","a","a"],
              "Cartas": [ { "Id": "a", "Evolucoes": [ { "Nome": "A1", "Xp": 5 }, { "Nome": "A2", "Xp": 3 } ] } ]
            }
            """;

        var erro = Assert.Throws<InvalidOperationException>(() => CatalogoCartas.Carregar(json));
        Assert.Contains("A2", erro.Message);
    }

    // ------------------------------------------------------------------ jogar carta

    [Fact]
    public void JogarCarta_DescontaMana_EColocaAProximaNoLugar()
    {
        var batalha = Nova(Enumerable.Range(0, 8).Select(i => Carta($"c{i}", c => c.Custo = 3)).ToList());
        var lado = batalha.LadoDe(Equipe.Jogador);

        var proxima = lado.Proxima;
        batalha.Jogar(Equipe.Jogador, 2, PertoDoNinho);
        batalha.Avancar();

        Assert.Same(proxima, lado.Mao[2]);
        Assert.Equal(Lado.ManaInicial + Batalha.ManaBasePorSegundo * Batalha.Passo - 3f, lado.Mana, 3);
        Assert.Single(batalha.Unidades);
    }

    [Fact]
    public void Baralho_CartaJogadaSoVoltaDepoisDeTodasAsOutras()
    {
        var cartas = Enumerable.Range(0, 8).Select(i => Carta($"c{i}", c => c.Custo = 0)).ToList();
        var batalha = Nova(cartas);
        var lado = batalha.LadoDe(Equipe.Jogador);

        var primeira = lado.Mao[0];
        var vistas = new List<CartaDef>();

        // Joga sempre o espaço 0: as 4 da fila entram uma a uma, e só então a primeira volta.
        for (int i = 0; i < 4; i++)
        {
            batalha.Jogar(Equipe.Jogador, 0, PertoDoNinho);
            batalha.Avancar();
            vistas.Add(lado.Mao[0]);
        }

        Assert.DoesNotContain(primeira, vistas);
        Assert.Equal(4, vistas.Distinct().Count());
        Assert.Same(primeira, lado.Proxima);

        batalha.Jogar(Equipe.Jogador, 0, PertoDoNinho);
        batalha.Avancar();
        Assert.Same(primeira, lado.Mao[0]);
    }

    [Fact]
    public void Criatura_LongeDoNinho_Recusa_MasFeiticoPode()
    {
        var batalha = Nova(SoCom(Carta("lobo")));
        var feitico = Nova(SoCom(Carta("fogo", c => c.Tipo = TipoDeCarta.Feitico)));

        Assert.False(batalha.PodeJogar(Equipe.Jogador, 0, new Vector2(9f, 5f), out string motivo));
        Assert.Equal("Fora da sua área", motivo);
        Assert.True(batalha.PodeJogar(Equipe.Jogador, 0, PertoDoNinho, out _));
        Assert.True(feitico.PodeJogar(Equipe.Jogador, 0, new Vector2(9f, 5f), out _));
    }

    [Fact]
    public void Carta_SemMana_Recusa()
    {
        var batalha = Nova(SoCom(Carta("cara", c => c.Custo = 9)));

        Assert.False(batalha.PodeJogar(Equipe.Jogador, 0, PertoDoNinho, out string motivo));
        Assert.Equal("Mana insuficiente", motivo);
    }

    [Fact]
    public void SantuarioDominado_AbreImplantacaoEmVoltaDeleSoPraQuemDomina()
    {
        var batalha = Nova();
        var santuario = batalha.Santuarios[0];
        var pertoDoSantuario = santuario.Posicao - new Vector2(0f, 2.5f);

        Assert.False(batalha.AreaDeImplantacao(Equipe.Jogador, pertoDoSantuario));

        Dominar(santuario, Equipe.Jogador);

        Assert.True(batalha.AreaDeImplantacao(Equipe.Jogador, pertoDoSantuario));
        Assert.False(batalha.AreaDeImplantacao(Equipe.Inimigo, pertoDoSantuario));
        Assert.False(batalha.AreaDeImplantacao(Equipe.Jogador, batalha.Santuarios[2].Posicao));
    }

    [Fact]
    public void SantuarioSobAtaque_FechaAImplantacaoEmVolta()
    {
        var batalha = Nova();
        var santuario = batalha.Santuarios[0];
        var pertoDoSantuario = santuario.Posicao - new Vector2(0f, 2.5f);
        Dominar(santuario, Equipe.Jogador);

        batalha.CriarUnidade(Equipe.Inimigo, Estatua("invasor"), santuario.Posicao, instantanea: true);
        batalha.Avancar();

        Assert.True(santuario.SobAtaque);
        Assert.False(batalha.AreaDeImplantacao(Equipe.Jogador, pertoDoSantuario));
    }

    // ------------------------------------------------------------------ santuários

    [Fact]
    public void Santuario_TropaSozinha_DominaNoTempoDeCaptura_EPontua()
    {
        var batalha = Nova();
        var santuario = batalha.Santuarios[1];
        batalha.CriarUnidade(Equipe.Jogador, Estatua("guarda"), santuario.Posicao, instantanea: true);

        Rodar(batalha, Batalha.TempoDeCaptura - 0.5f);
        Assert.Null(santuario.Dono);

        Rodar(batalha, 1f);
        Assert.Equal(Equipe.Jogador, santuario.Dono);

        var eventos = new List<EventoDeBatalha>();
        batalha.ColherEventos(eventos);
        Assert.Contains(eventos, e => e.Tipo == TipoDeEvento.Capturou && e.Equipe == Equipe.Jogador);

        float pontos = batalha.LadoDe(Equipe.Jogador).Pontos;
        Rodar(batalha, 2f);
        Assert.Equal(pontos + 2f * Batalha.PontosPorSegundo, batalha.LadoDe(Equipe.Jogador).Pontos, 0.05f);
    }

    [Fact]
    public void Santuario_ComTropaDasDuasEquipes_TravaNaDisputa()
    {
        var batalha = Nova();
        var santuario = batalha.Santuarios[0];
        batalha.CriarUnidade(Equipe.Jogador, Estatua("a"), santuario.Posicao - new Vector2(0.5f, 0f), instantanea: true);
        batalha.CriarUnidade(Equipe.Inimigo, Estatua("b"), santuario.Posicao + new Vector2(0.5f, 0f), instantanea: true);

        Rodar(batalha, 3f);

        Assert.True(santuario.Disputado);
        Assert.Equal(0f, santuario.Influencia);
    }

    [Fact]
    public void SantuarioDoOponente_PrecisaNeutralizarAntesDeVirar()
    {
        var batalha = Nova();
        var santuario = batalha.Santuarios[2];
        Dominar(santuario, Equipe.Inimigo);
        batalha.CriarUnidade(Equipe.Jogador, Estatua("guarda"), santuario.Posicao, instantanea: true);

        Rodar(batalha, Batalha.TempoDeCaptura + 0.5f);
        Assert.Null(santuario.Dono);
        Assert.True(santuario.Influencia > 0f);

        Rodar(batalha, Batalha.TempoDeCaptura);
        Assert.Equal(Equipe.Jogador, santuario.Dono);
        Assert.Equal(1, batalha.LadoDe(Equipe.Jogador).Capturas);
    }

    [Fact]
    public void SantuarioDominado_AceleraAMana()
    {
        var batalha = Nova();
        Dominar(batalha.Santuarios[0], Equipe.Jogador);

        var jogador = batalha.LadoDe(Equipe.Jogador);
        var inimigo = batalha.LadoDe(Equipe.Inimigo);
        jogador.Mana = 0f;
        inimigo.Mana = 0f;

        Rodar(batalha, 2f);

        Assert.Equal((Batalha.ManaBasePorSegundo + Batalha.ManaPorSantuarioPorSegundo) * 2f, jogador.Mana, 0.02f);
        Assert.Equal(Batalha.ManaBasePorSegundo * 2f, inimigo.Mana, 0.02f);
    }

    [Fact]
    public void Ninho_QueimaInvasor_ECuraQuemEDaCasa()
    {
        var batalha = Nova();
        var ninho = Campo.PosicaoNinho(Equipe.Jogador);

        var invasor = batalha.CriarUnidade(Equipe.Inimigo, Estatua("invasor", c => { c.Vida = 1000f; c.Visao = 0f; }),
            ninho + new Vector2(2.5f, 0f), instantanea: true);
        var ferido = batalha.CriarUnidade(Equipe.Jogador, Estatua("ferido", c => { c.Vida = 1000f; c.Visao = 0f; }),
            ninho - new Vector2(2.5f, 0f), instantanea: true);
        ferido.Vida = 500f;

        Rodar(batalha, 1f);

        Assert.Equal(1000f - Batalha.DanoDoNinhoPorSegundo, invasor.Vida, 5f);
        Assert.True(ferido.Vida > 500f);
    }

    // ------------------------------------------------------------------ movimento e combate

    [Fact]
    public void UnidadeDeChao_ContornaPedra_EChegaNoSantuario()
    {
        var batalha = Nova();
        var corredor = Carta("corredor", c => { c.Vida = 100_000f; c.Velocidade = 3f; });
        var unidade = batalha.CriarUnidade(Equipe.Jogador, corredor, Campo.Pedras[0].Centro + new Vector2(0f, 2.2f), instantanea: true);
        bool chegou = false;

        Rodar(batalha, 10f, () =>
        {
            foreach (var obstaculo in Campo.Obstaculos)
            {
                Assert.True(Vector2.Distance(unidade.Posicao, obstaculo.Centro) >= obstaculo.Raio + unidade.Raio - 0.05f,
                    $"Unidade dentro do obstáculo {obstaculo} em {unidade.Posicao}.");
            }

            chegou |= batalha.Santuarios.Any(s => s.Contem(unidade.Posicao));
        });

        Assert.True(chegou);
    }

    [Fact]
    public void DonoDosTres_SemNadaPraTomar_MarchaProNinhoInimigo()
    {
        var batalha = Nova();
        foreach (var santuario in batalha.Santuarios)
            Dominar(santuario, Equipe.Jogador);

        var unidade = batalha.CriarUnidade(Equipe.Jogador, Carta("marchador", c => { c.Vida = 100_000f; c.Velocidade = 3f; }),
            PertoDoNinho, instantanea: true);
        var ninhoInimigo = Campo.PosicaoNinho(Equipe.Inimigo);
        float antes = Vector2.Distance(unidade.Posicao, ninhoInimigo);

        Rodar(batalha, 3f);

        Assert.True(Vector2.Distance(unidade.Posicao, ninhoInimigo) < antes - 6f,
            $"Andou de {antes:0.0} pra {Vector2.Distance(unidade.Posicao, ninhoInimigo):0.0} do ninho inimigo.");
    }

    [Fact]
    public void Evolucao_AoMatar_SobeEstagio_CuraECresce()
    {
        var batalha = Nova();
        var cacador = Carta("cacador", c =>
        {
            c.Vida = 400f;
            c.Dano = 1000f;
            c.Alcance = 1f;
            c.Evolucoes = [new EvolucaoDef { Nome = "Cacador Alfa", Xp = 1, Vida = 2f, Escala = 1.2f }];
        });
        var presa = Carta("presa", c => { c.Vida = 50f; c.XpAoMorrer = 1; });

        var unidade = batalha.CriarUnidade(Equipe.Jogador, cacador, new Vector2(9f, 14.4f), instantanea: true);
        batalha.CriarUnidade(Equipe.Inimigo, presa, new Vector2(9f, 13.5f), instantanea: true);

        var eventos = new List<EventoDeBatalha>();
        Rodar(batalha, 1f);
        batalha.ColherEventos(eventos);

        Assert.Equal(1, unidade.Estagio);
        Assert.Equal("Cacador Alfa", unidade.Nome);
        Assert.Equal(800f, unidade.VidaMaxima);
        Assert.Equal(0.45f * 1.2f, unidade.Raio, 3);
        Assert.Equal(1, batalha.LadoDe(Equipe.Jogador).Evolucoes);
        Assert.Contains(eventos, e => e.Tipo == TipoDeEvento.Evoluiu && e.Texto == "Cacador Alfa");
    }

    [Fact]
    public void Evolucao_AoDominarSantuario_ComXpAoCapturar()
    {
        var batalha = Nova();
        var santuario = batalha.Santuarios[1];
        var tanque = Estatua("tanque", c =>
        {
            c.XpAoCapturar = 3;
            c.Evolucoes = [new EvolucaoDef { Nome = "Tanque Blindado", Xp = 3 }];
        });

        var unidade = batalha.CriarUnidade(Equipe.Jogador, tanque, santuario.Posicao, instantanea: true);
        Rodar(batalha, Batalha.TempoDeCaptura + 0.5f);

        Assert.Equal(Equipe.Jogador, santuario.Dono);
        Assert.Equal(1, unidade.Estagio);
    }

    [Fact]
    public void Abate_DevolveFracaoDoCusto_EEvoluidaDaRecompensa()
    {
        var batalha = Nova();
        var cacador = Carta("cacador", c => { c.Dano = 5000f; c.Alcance = 1f; });
        var ameaca = Carta("ameaca", c =>
        {
            c.Custo = 4;
            c.Vida = 300f;
            c.Evolucoes = [new EvolucaoDef { Nome = "Ameaca Maior", Xp = 1, Recompensa = 2 }];
        });

        batalha.CriarUnidade(Equipe.Jogador, cacador, new Vector2(9f, 14.4f), instantanea: true);
        var evoluida = batalha.CriarUnidade(Equipe.Inimigo, ameaca, new Vector2(9f, 13.5f), instantanea: true);
        batalha.GanharXp(evoluida, 1);

        var jogador = batalha.LadoDe(Equipe.Jogador);
        jogador.Mana = 0f;

        Rodar(batalha, 1f);

        Assert.False(evoluida.Viva);
        Assert.Equal(2, jogador.ManaDeRecompensa);
        Assert.Equal(Batalha.ManaPorAbate * 4f, jogador.ManaDeAbates, 0.001f);
        Assert.True(jogador.Mana >= 2f + Batalha.ManaPorAbate * 4f, $"Mana {jogador.Mana} deveria ter abate + recompensa.");
    }

    [Fact]
    public void Feitico_DeLentidao_ReduzAVelocidadeDoAlvo()
    {
        var lamacal = Carta("lamacal", c =>
        {
            c.Tipo = TipoDeCarta.Feitico;
            c.Raio = 2f;
            c.Dano = 1f;
            c.Lentidao = 0.5f;
            c.LentidaoDuracao = 5f;
        });
        var batalha = Nova(SoCom(lamacal));
        var alvo = batalha.CriarUnidade(Equipe.Inimigo, Carta("alvo", c => { c.Vida = 1000f; c.Velocidade = 2f; }),
            new Vector2(9f, 6f), instantanea: true);

        batalha.Jogar(Equipe.Jogador, 0, alvo.Posicao);
        Rodar(batalha, 0.2f);

        Assert.True(alvo.Lenta > 0f);
        Assert.Equal(1f, alvo.Velocidade, 3);
    }

    // ------------------------------------------------------------------ fim

    [Fact]
    public void PontosNaMeta_VitoriaNaHora_EParaDeAvancar()
    {
        var batalha = Nova();
        Dominar(batalha.Santuarios[1], Equipe.Jogador);
        batalha.LadoDe(Equipe.Jogador).Pontos = Batalha.MetaDePontos - 0.5f;

        Rodar(batalha, 1f);

        Assert.Equal(ResultadoDaBatalha.VitoriaDoJogador, batalha.Resultado);

        int passo = batalha.PassoAtual;
        Rodar(batalha, 1f);
        Assert.Equal(passo, batalha.PassoAtual);
    }

    [Fact]
    public void TempoAcaba_SemPontos_Empate()
    {
        var batalha = Nova();

        Rodar(batalha, Batalha.Duracao + 0.5f);

        Assert.Equal(ResultadoDaBatalha.Empate, batalha.Resultado);
        Assert.Equal(Lado.ManaMaxima, batalha.LadoDe(Equipe.Jogador).Mana);
    }

    [Fact]
    public void TempoAcaba_QuemTemMaisPontosVence()
    {
        var batalha = Nova();
        batalha.LadoDe(Equipe.Inimigo).Pontos = 10f;

        Rodar(batalha, Batalha.Duracao + 0.5f);

        Assert.Equal(ResultadoDaBatalha.VitoriaDoInimigo, batalha.Resultado);
    }

    // ------------------------------------------------------------------ IA e determinismo

    private static Batalha IaContraIa(int semente, float segundos)
    {
        var baralho = CatalogoReal().Baralho();
        var batalha = new Batalha(baralho, baralho, semente);
        var azul = new IaOponente(batalha, Equipe.Jogador, Dificuldade.Normal, semente + 1);
        var vermelha = new IaOponente(batalha, Equipe.Inimigo, Dificuldade.Normal, semente + 2);

        Rodar(batalha, segundos, () =>
        {
            azul.Passo();
            vermelha.Passo();
        });

        return batalha;
    }

    [Fact]
    public void MesmaSemente_EMesmasDecisoes_MesmaPartida()
    {
        var a = IaContraIa(semente: 42, segundos: 90f);
        var b = IaContraIa(semente: 42, segundos: 90f);

        Assert.Equal(a.Assinatura(), b.Assinatura());
    }

    [Fact]
    public void IaContraIa_DisputaSantuarios_EAPartidaTermina()
    {
        var batalha = IaContraIa(semente: 3, segundos: Batalha.Duracao + 1f);

        Assert.True(batalha.Acabou);
        Assert.True(batalha.LadoDe(Equipe.Jogador).Capturas + batalha.LadoDe(Equipe.Inimigo).Capturas > 0);
    }
}
