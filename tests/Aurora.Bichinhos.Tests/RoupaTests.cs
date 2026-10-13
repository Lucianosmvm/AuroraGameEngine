using System.Numerics;
using Bichinhos;

namespace BichinhosTests;

/// <summary>Itens vestíveis: catálogo, âncoras, posição na tela, loja, presente do 7º dia e save.</summary>
public class RoupaTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "Assets");

    public RoupaTests()
    {
        Catalogo.Atual = Catalogo.Carregar(File.ReadAllText(Path.Combine(Assets, Catalogo.Caminho)));
        Ancoras.Atual = Ancoras.Carregar(File.ReadAllText(Path.Combine(Assets, Ancoras.Caminho)));
    }

    private static Progresso Com(int moedas) => new() { Moedas = moedas };

    // ------------------------------------------------------------------ catálogo

    [Fact]
    public void Catalogo_IdsUnicosESpritesExistem()
    {
        Assert.Equal(Itens.Todos.Count, Itens.Todos.Select(i => i.Id).Distinct().Count());
        foreach (var item in Itens.Todos)
            Assert.True(File.Exists(Path.Combine(Assets, item.Sprite)), item.Sprite);

        Assert.True(File.Exists(Path.Combine(Assets, "sprites/icones/roupa.png")));
    }

    [Fact]
    public void Catalogo_DesenhoCombinaComOLugarEOPrecoComOTipo()
    {
        foreach (var item in Itens.Todos)
        {
            Assert.Equal(item.Slot, item.Desenho switch
            {
                Desenho.Topo => Slot.Cabeca,
                Desenho.Lentes => Slot.Rosto,
                _ => Slot.Pescoco,
            });
            Assert.Equal(item.Exclusivo, item.Preco == 0);
        }

        Assert.Equal([Itens.Coroa], Itens.Todos.Where(i => i.Exclusivo));
        Assert.Null(Itens.Achar("nao-existe"));
        Assert.Null(Itens.Achar(null));
    }

    // ------------------------------------------------------------------ âncoras

    [Fact]
    public void Ancoras_TodaFormaDeTodaEspecieTemEncaixeValido()
    {
        foreach (var especie in Catalogo.Atual.Especies)
        {
            for (int estagio = 0; estagio < especie.Estagios.Count; estagio++)
            {
                var forma = Ancoras.Atual.Forma(especie.Id, estagio);
                Assert.True(forma is not null, $"{especie.Id}_{estagio} sem âncora (rode Arte/gerar_ancoras.py)");

                foreach (var a in new[] { forma!.Cabeca, forma.Pescoco })
                {
                    Assert.InRange(a.X, 20f, 80f);
                    Assert.InRange(a.Y, 0f, 100f);
                    Assert.InRange(a.Largura, 8f, 60f);
                }

                // A cabeça fica acima do pescoço e os olhos entre os dois.
                Assert.True(forma.Cabeca.Y < especie.Estagios[estagio].Olhos[1], $"{especie.Id}_{estagio}: topo abaixo dos olhos");
                Assert.True(forma.Pescoco.Y > especie.Estagios[estagio].Olhos[1], $"{especie.Id}_{estagio}: pescoço acima dos olhos");
            }
        }
    }

    [Fact]
    public void Ancoras_TodoItemTemConfiguracaoESemEncaixeDesconhecidoNaoQuebra()
    {
        foreach (var item in Itens.Todos)
            Assert.True(Ancoras.Atual.Itens.ContainsKey(item.Id), $"{item.Id} sem configuração");

        Assert.Null(Ancoras.Atual.Forma("especie-que-nao-existe", 0));
        Assert.Equal(1f, Ancoras.Atual.Config("nao-existe").Escala);
    }

    // ------------------------------------------------------------------ posição

    private static readonly FormaAncoras Forma = new(new Ancora(50f, 20f, 40f), new Ancora(50f, 70f, 30f));
    private static readonly float[] Olhos = [50f, 60f, 10f, 5f];
    private static readonly Vector2 Pos = new(100f, 180f);
    private static readonly Vector2 Tamanho = new(200f, 200f);

    private static void Perto(float esperado, float real) => Assert.InRange(real, esperado - 0.01f, esperado + 0.01f);

    [Fact]
    public void Layout_ChapeuPousaNoTopoDaCabecaComALarguraDaAncoraVezesAEscala()
    {
        var pecas = RoupaLayout.Montar(Itens.Chapeu, new ConfigItem(0.5f, 0f, 0f), Forma, Olhos, Pos, Tamanho, false, out var ponte);

        var peca = Assert.Single(pecas);
        Assert.Null(ponte);
        Perto(100f, peca.Pos.X);                      // centro do sprite
        Perto(180f + (0.20f - 0.9f) * 200f, peca.Pos.Y);   // y = 20% do sprite, relativo ao pivô em 90%
        Perto(40f, peca.Tamanho.X);                   // 40% de 200 = 80, x 0,5 = 40
        Perto(RoupaLayout.PivoTopo, peca.Pivo.Y);
        Perto(0f, peca.Rotacao);
    }

    [Fact]
    public void Layout_EspelharInverteODeslocamentoELaco()
    {
        var forma = new FormaAncoras(new Ancora(30f, 20f, 40f), Forma.Pescoco);
        var config = new ConfigItem(0.5f, 0.5f, 0.35f);

        var normal = RoupaLayout.Montar(Itens.Laco, config, forma, Olhos, Pos, Tamanho, false, out _).Single();
        var espelhado = RoupaLayout.Montar(Itens.Laco, config, forma, Olhos, Pos, Tamanho, true, out _).Single();

        // x = 30% do sprite -> 100 + (0,3-0,5)*200 = 60, mais 0,5*40%*200 = 40 de deslocamento
        Perto(100f, normal.Pos.X);
        // espelhado: x = 70% -> 140, deslocamento para o outro lado: 140 - 40
        Perto(100f, espelhado.Pos.X);
        Perto(0.35f, normal.Rotacao);
        Perto(-0.35f, espelhado.Rotacao);
        Perto(RoupaLayout.PivoLaco, normal.Pivo.Y);
    }

    [Fact]
    public void Layout_LenteEmCadaOlhoELiguadasPorUmaPonte()
    {
        var pecas = RoupaLayout.Montar(Itens.Oculos, new ConfigItem(1f, 0f, 0f), Forma, Olhos, Pos, Tamanho, false, out var ponte);

        Assert.Equal(2, pecas.Count);
        Perto(100f + (0.40f - 0.5f) * 200f, pecas[0].Pos.X);   // 80
        Perto(100f + (0.60f - 0.5f) * 200f, pecas[1].Pos.X);   // 120
        Perto(180f + (0.60f - 0.9f) * 200f, pecas[0].Pos.Y);   // 120
        Perto(120f, pecas[0].Pos.Y);

        float lado = 5f * RoupaLayout.FatorLente / 100f * 200f;
        Perto(lado, pecas[0].Tamanho.X);
        Perto(pecas[0].Tamanho.X, pecas[0].Tamanho.Y);

        var p = Assert.NotNull(ponte);
        Perto(80f + lado / 2f * 0.9f, p.Pos.X);
        Perto(120f - lado / 2f * 0.9f - p.Pos.X, p.Tamanho.X);
        Assert.True(p.Tamanho.Y >= 2f);
    }

    [Fact]
    public void Layout_OculosEspelhadosFicamSimetricosEmTornoDoCentroDoBicho()
    {
        var a = RoupaLayout.Montar(Itens.OculosSol, new ConfigItem(1f, 0f, 0f), Forma, Olhos, Pos, Tamanho, false, out _);
        var b = RoupaLayout.Montar(Itens.OculosSol, new ConfigItem(1f, 0f, 0f), Forma, Olhos, Pos, Tamanho, true, out _);

        // Olhos centrados no sprite (50%): espelhar não muda ONDE as lentes caem. A ordem da lista
        // troca (a lente "da esquerda" passa a ser a da direita), então compara-se o conjunto.
        var xsA = a.Select(p => p.Pos.X).Order().ToArray();
        var xsB = b.Select(p => p.Pos.X).Order().ToArray();
        Perto(xsA[0], xsB[0]);
        Perto(xsA[1], xsB[1]);
    }

    [Fact]
    public void Layout_LencoUsaOPescocoEOPivoDaFaixa()
    {
        var peca = RoupaLayout.Montar(Itens.Lenco, new ConfigItem(0.85f, 0f, 0f), Forma, Olhos, Pos, Tamanho, false, out _).Single();

        Perto(180f + (0.70f - 0.9f) * 200f, peca.Pos.Y);   // 140
        Perto(30f / 100f * 200f * 0.85f, peca.Tamanho.X);
        Perto(RoupaLayout.PivoFaixa, peca.Pivo.Y);
    }

    [Fact]
    public void Layout_RespiracaoEsticaOBichoEOItemAcompanha()
    {
        var normal = RoupaLayout.Montar(Itens.Chapeu, new ConfigItem(0.6f, 0f, 0f), Forma, Olhos, Pos, Tamanho, false, out _).Single();
        var esticado = RoupaLayout.Montar(Itens.Chapeu, new ConfigItem(0.6f, 0f, 0f), Forma, Olhos, Pos, Tamanho * new Vector2(0.98f, 1.03f), false, out _).Single();

        Assert.True(esticado.Pos.Y < normal.Pos.Y);          // a cabeça subiu junto
        Assert.True(esticado.Tamanho.X < normal.Tamanho.X);  // e o chapéu afinou com o corpo
    }

    // ------------------------------------------------------------------ loja

    [Fact]
    public void Loja_CompraDescontaMoedasGuardaOItemEImpedeRepetir()
    {
        var p = Com(40);

        Assert.Equal(ResultadoCompra.Ok, Loja.Comprar(p, Itens.Chapeu));
        Assert.Equal(25, p.Moedas);
        Assert.True(Loja.Tem(p, Itens.Chapeu));

        Assert.Equal(ResultadoCompra.JaTem, Loja.Comprar(p, Itens.Chapeu));
        Assert.Equal(25, p.Moedas);
    }

    [Fact]
    public void Loja_SemMoedasNaoCompraEExclusivoNaoSeVende()
    {
        var p = Com(10);

        Assert.Equal(ResultadoCompra.SemMoedas, Loja.Comprar(p, Itens.Lenco));
        Assert.Equal(10, p.Moedas);
        Assert.False(Loja.Tem(p, Itens.Lenco));

        p.Moedas = 9999;
        Assert.Equal(ResultadoCompra.NaoSeVende, Loja.Comprar(p, Itens.Coroa));
        Assert.Equal(9999, p.Moedas);
    }

    [Fact]
    public void Loja_UmItemPorLugarMasLugaresDiferentesConvivem()
    {
        var p = Com(500);
        foreach (var item in new[] { Itens.Chapeu, Itens.Laco, Itens.Oculos, Itens.Lenco })
            Loja.Comprar(p, item);

        Assert.True(Loja.Equipar(p, Itens.Chapeu));
        Assert.True(Loja.Equipar(p, Itens.Oculos));
        Assert.True(Loja.Equipar(p, Itens.Lenco));
        Assert.Equal(3, p.Roupa.Vestidos().Count());

        Assert.True(Loja.Equipar(p, Itens.Laco));   // troca o chapéu
        Assert.False(Loja.Equipado(p, Itens.Chapeu));
        Assert.True(Loja.Equipado(p, Itens.Laco));
        Assert.Equal(3, p.Roupa.Vestidos().Count());

        Loja.Tirar(p, Slot.Rosto);
        Assert.Null(p.Roupa.Rosto);
        Assert.Equal(2, p.Roupa.Vestidos().Count());
    }

    [Fact]
    public void Loja_NaoVesteOQueNaoTem()
    {
        var p = Com(0);
        Assert.False(Loja.Equipar(p, Itens.Oculos));
        Assert.Null(p.Roupa.Rosto);
    }

    [Fact]
    public void Roupa_IdDeItemQueNaoExisteMaisEIgnorado()
    {
        var roupa = new Roupa { Cabeca = "chapeu-antigo-removido", Rosto = Itens.Oculos.Id };
        Assert.Equal([Itens.Oculos], roupa.Vestidos());
    }

    // ------------------------------------------------------------------ presente do 7º dia

    private static DateOnly Dia(int d) => new(2026, 3, 1 + d);

    [Fact]
    public void Presente_SoOSetimoDiaDaACoroaESoUmaVez()
    {
        var p = Com(0);

        for (int d = 0; d < 6; d++)
            Assert.Null(Presente.Receber(p, Dia(d))!.Value.Item);
        Assert.False(Loja.Tem(p, Itens.Coroa));

        var setimo = Presente.Receber(p, Dia(6))!.Value;
        Assert.Equal(Itens.Coroa.Id, setimo.Item);
        Assert.Equal(30, setimo.Moedas);
        Assert.True(Loja.Tem(p, Itens.Coroa));

        // Segundo ciclo: o 14º dia paga igual, mas a coroa ele já tem.
        for (int d = 7; d < 13; d++)
            Presente.Receber(p, Dia(d));
        var catorze = Presente.Receber(p, Dia(13))!.Value;
        Assert.Equal(7, catorze.Dia);
        Assert.Null(catorze.Item);
        Assert.Single(p.Guardados, Itens.Coroa.Id);
    }

    [Fact]
    public void Presente_PendenteAvisaDaCoroaSemEntregar()
    {
        var p = Com(0);
        for (int d = 0; d < 6; d++)
            Presente.Receber(p, Dia(d));

        Assert.Equal(Itens.Coroa.Id, Presente.Pendente(p, Dia(6))!.Value.Item);
        Assert.False(Loja.Tem(p, Itens.Coroa));
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public void Save_GuardadosERoupaSobrevivemAoDisco()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "bichinhos-roupa-" + Guid.NewGuid().ToString("N"));
        try
        {
            var p = Com(100);
            Loja.Comprar(p, Itens.Oculos);
            Loja.Comprar(p, Itens.Chapeu);
            Loja.Equipar(p, Itens.Oculos);
            Loja.Equipar(p, Itens.Chapeu);
            p.Salvar(pasta);

            var lido = Progresso.Carregar(pasta);

            Assert.Equal(["oculos", "chapeu"], lido.Guardados);
            Assert.Equal("chapeu", lido.Roupa.Cabeca);
            Assert.Equal("oculos", lido.Roupa.Rosto);
            Assert.Null(lido.Roupa.Pescoco);
        }
        finally
        {
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Save_ComBichoNovoMantemOGuardaRoupaMasNaoCompartilhaAReferencia()
    {
        var p = Com(100);
        Loja.Comprar(p, Itens.Lenco);
        Loja.Equipar(p, Itens.Lenco);

        var novo = p.ParaNovoBicho(new Bicho { EspecieId = "gotinha" });

        Assert.Equal(["lenco"], novo.Guardados);
        Assert.Equal("lenco", novo.Roupa.Pescoco);

        Loja.Tirar(novo, Slot.Pescoco);
        Assert.Equal("lenco", p.Roupa.Pescoco);   // cópia: mexer no novo não mexe no velho
    }

    [Fact]
    public void Save_AntigoSemOsCamposNovosComecaSemRoupa()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "bichinhos-antigo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, Progresso.Arquivo), """{ "Moedas": 7 }""");
            var p = Progresso.Carregar(pasta);

            Assert.Empty(p.Guardados);
            Assert.Empty(p.Roupa.Vestidos());
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }
}
