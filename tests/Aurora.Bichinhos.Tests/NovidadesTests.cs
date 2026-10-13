using System.Text.RegularExpressions;
using Bichinhos;

namespace BichinhosTests;

/// <summary>
/// Nome do bichinho, presente diário, "pegar frutas" e os sons. Tudo regra pura, sem janela.
/// </summary>
public class NovidadesTests
{
    private static readonly string Assets = Path.Combine(AppContext.BaseDirectory, "Assets");

    public NovidadesTests()
        => Catalogo.Atual = Catalogo.Carregar(File.ReadAllText(Path.Combine(Assets, Catalogo.Caminho)));

    // ------------------------------------------------------------------ nome

    [Fact]
    public void Nome_SemApelidoUsaAFormaComNoSaveAntigo()
    {
        var b = new Bicho { EspecieId = "brasinha", Estagio = 2 };
        Assert.Equal("Vulcanossauro", b.Nome);
    }

    [Fact]
    public void Nome_ComApelidoUsaOApelidoEGuardaAForma()
    {
        var b = new Bicho { EspecieId = "brasinha", Estagio = 2, Apelido = "Fumaça" };
        Assert.Equal("Fumaça", b.Nome);
        Assert.Equal("Vulcanossauro", b.NomeDaForma);
    }

    [Fact]
    public void Nomes_SortearNuncaRepeteOQueJaEstaNaTela()
    {
        var rng = new Random(1);
        string atual = Nomes.Todos[0];
        for (int i = 0; i < 200; i++)
        {
            string novo = Nomes.Sortear(rng, atual);
            Assert.NotEqual(atual, novo);
            Assert.Contains(novo, Nomes.Todos);
            atual = novo;
        }
    }

    [Fact]
    public void Nomes_SemRepetidosNemVazios()
    {
        Assert.Equal(Nomes.Todos.Length, Nomes.Todos.Distinct().Count());
        Assert.All(Nomes.Todos, n => Assert.False(string.IsNullOrWhiteSpace(n)));
    }

    // ------------------------------------------------------------------ presente

    private static DateOnly Dia(int d) => new(2026, 3, 1 + d);

    [Fact]
    public void Presente_PrimeiroDiaPagaCincoESeEsgotaNoMesmoDia()
    {
        var p = new Progresso { Moedas = 0 };

        var primeiro = Presente.Receber(p, Dia(0));

        Assert.Equal(new PresenteDoDia(1, 5, 1), primeiro);
        Assert.Equal(5, p.Moedas);
        Assert.Null(Presente.Pendente(p, Dia(0)));
        Assert.Null(Presente.Receber(p, Dia(0)));
        Assert.Equal(5, p.Moedas);
    }

    [Fact]
    public void Presente_DiasSeguidosSobemAteOSetimoQuePagaMais()
    {
        var p = new Progresso { Moedas = 0 };
        for (int d = 0; d < 7; d++)
            Assert.Equal(Presente.MoedasPorDia[d], Presente.Receber(p, Dia(d))!.Value.Moedas);

        Assert.Equal(5 + 7 + 9 + 11 + 13 + 15 + 30, p.Moedas);
        Assert.Equal(7, p.SequenciaDias);
    }

    [Fact]
    public void Presente_DepoisDoSetimoDiaRecomecaOCicloMasMantemASequencia()
    {
        var p = new Progresso();
        for (int d = 0; d < 8; d++)
            Presente.Receber(p, Dia(d));

        Assert.Equal(8, p.SequenciaDias);
        var nono = Presente.Pendente(p, Dia(8))!.Value;
        Assert.Equal(2, nono.Dia);            // 9º dia seguido = 2º do ciclo
        Assert.Equal(7, nono.Moedas);
    }

    [Fact]
    public void Presente_FaltarUmDiaZeraASequencia()
    {
        var p = new Progresso();
        Presente.Receber(p, Dia(0));
        Presente.Receber(p, Dia(1));

        var depoisDaFalta = Presente.Receber(p, Dia(4))!.Value;

        Assert.Equal(1, depoisDaFalta.Sequencia);
        Assert.Equal(5, depoisDaFalta.Moedas);
    }

    [Fact]
    public void Presente_RelogioVoltadoNaoDaPresenteDeNovo()
    {
        var p = new Progresso();
        Presente.Receber(p, Dia(5));
        Assert.Null(Presente.Pendente(p, Dia(3)));
    }

    [Fact]
    public void Presente_SaveComDataIlegivelContaComoPrimeiraVez()
    {
        var p = new Progresso { UltimoPresente = "lixo" };
        Assert.Equal(1, Presente.Pendente(p, Dia(0))!.Value.Sequencia);
    }

    [Fact]
    public void Progresso_ParaNovoBichoNaoZeraSequenciaNemSom()
    {
        var p = new Progresso { SomLigado = false, SequenciaDias = 4, UltimoPresente = "2026-03-01", RecordePegar = 17, Moedas = 500 };

        var novo = p.ParaNovoBicho(new Bicho { EspecieId = "gotinha" });

        Assert.False(novo.SomLigado);
        Assert.Equal(4, novo.SequenciaDias);
        Assert.Equal("2026-03-01", novo.UltimoPresente);
        Assert.Equal(17, novo.RecordePegar);
        Assert.Equal(20, novo.Moedas);   // moedas voltam ao inicial: o bicho novo começa do zero
    }

    [Fact]
    public void Progresso_SaveAntigoSemOsCamposNovosCarregaComPadroes()
    {
        string pasta = Path.Combine(Path.GetTempPath(), "bichinhos-teste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            File.WriteAllText(Path.Combine(pasta, Progresso.Arquivo), """{ "Moedas": 42, "Bicho": { "EspecieId": "brasinha", "Nivel": 3 } }""");

            var p = Progresso.Carregar(pasta);

            Assert.Equal(42, p.Moedas);
            Assert.True(p.SomLigado);
            Assert.Equal(0, p.SequenciaDias);
            Assert.Equal("", p.Bicho!.Apelido);
            Assert.NotNull(Presente.Pendente(p, Dia(0)));
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    // ------------------------------------------------------------------ pegar frutas

    private static ItemCaindo Item(TipoItem tipo, float x, float y = PegaFrutas.LinhaDaBoca - 1f)
        => new() { Tipo = tipo, X = x, Y = y, Velocidade = 400f };

    private static PegaFrutas Jogo(int semente = 1) => new(new Random(semente));

    [Fact]
    public void Pegar_BichoSegueOAlvoSemSairDosLimites()
    {
        var j = Jogo();
        j.Atualizar(0.05f, -500f);
        Assert.True(j.BichoX >= PegaFrutas.Margem);

        for (int i = 0; i < 100; i++)
            j.Atualizar(0.05f, 5000f);
        Assert.Equal(PegaFrutas.Largura - PegaFrutas.Margem, j.BichoX);
    }

    [Fact]
    public void Pegar_FrutaNaBocaSomaPontoEForaDaBocaNaoConta()
    {
        var j = Jogo();
        j.Itens.Clear();
        j.Itens.Add(Item(TipoItem.Maca, j.BichoX));
        j.Itens.Add(Item(TipoItem.Doce, j.BichoX + PegaFrutas.RaioDaBoca + 60f));

        j.Atualizar(0.05f, j.BichoX);

        Assert.Equal(1, j.Pontos);
        var eventos = j.DrenarEventos();
        Assert.Contains(eventos, e => e.Tipo == TipoItem.Maca && e.Pegou);
        Assert.DoesNotContain(eventos, e => e.Tipo == TipoItem.Doce && e.Pegou);
    }

    [Fact]
    public void Pegar_ValoresDosItens()
    {
        Assert.Equal(1, PegaFrutas.ValorDe(TipoItem.Maca));
        Assert.Equal(3, PegaFrutas.ValorDe(TipoItem.Doce));
        Assert.Equal(5, PegaFrutas.ValorDe(TipoItem.Estrela));
        Assert.Equal(-2, PegaFrutas.ValorDe(TipoItem.Coco));
    }

    [Fact]
    public void Pegar_CocoTiraPontosMasNuncaDeixaNegativo()
    {
        var j = Jogo();
        j.Itens.Add(Item(TipoItem.Coco, j.BichoX));
        j.Atualizar(0.05f, j.BichoX);
        Assert.Equal(0, j.Pontos);

        j.Itens.Add(Item(TipoItem.Estrela, j.BichoX));
        j.Atualizar(0.05f, j.BichoX);
        j.Itens.Add(Item(TipoItem.Coco, j.BichoX));
        j.Atualizar(0.05f, j.BichoX);
        Assert.Equal(3, j.Pontos);          // 5 - 2
    }

    [Fact]
    public void Pegar_FrutaQueCaiNoChaoSomeSemCustoEViraEvento()
    {
        var j = Jogo();
        j.Itens.Add(Item(TipoItem.Maca, 40f, PegaFrutas.Chao - 1f));   // longe do bicho
        j.Atualizar(0.05f, j.BichoX);

        Assert.Equal(0, j.Pontos);
        Assert.DoesNotContain(j.Itens, i => i.X == 40f);
        Assert.Contains(j.DrenarEventos(), e => !e.Pegou);
    }

    [Fact]
    public void Pegar_TerminaEmVinteSegundosEParaDeMexer()
    {
        var j = Jogo();
        for (int i = 0; i < 700 && !j.Terminou; i++)
            j.Atualizar(1f / 30f, j.BichoX);

        Assert.True(j.Terminou);
        Assert.InRange(j.Tempo, PegaFrutas.Duracao, PegaFrutas.Duracao + 0.1f);

        int pontos = j.Pontos;
        j.Atualizar(1f, 100f);
        Assert.Equal(pontos, j.Pontos);
    }

    [Fact]
    public void Pegar_PartidaInteiraSoltaItensDeTodosOsTiposESeguindoAsFrutasSeFazPonto()
    {
        var j = Jogo(3);
        var vistos = new HashSet<TipoItem>();
        for (int i = 0; i < 700 && !j.Terminou; i++)
        {
            // Um jogador perfeito: vai sempre para a fruta que está mais perto de chegar à boca.
            var alvo = j.Itens.Where(x => x.Tipo != TipoItem.Coco).OrderByDescending(x => x.Y).FirstOrDefault();
            j.Atualizar(1f / 30f, alvo?.X ?? j.BichoX);
            foreach (var item in j.Itens) vistos.Add(item.Tipo);
        }

        Assert.True(j.Pontos >= PegaFrutas.PontosParaGanhar, $"só fez {j.Pontos} pontos");
        Assert.Contains(TipoItem.Maca, vistos);
        Assert.Contains(TipoItem.Coco, vistos);
    }

    [Theory]
    [InlineData(0, 0, 3, false)]
    [InlineData(9, 3, 7, false)]
    [InlineData(10, 3, 8, true)]
    [InlineData(30, 10, 18, true)]
    [InlineData(300, 10, 153, true)]   // moedas têm teto
    public void Pegar_Recompensa(int pontos, int moedas, int xp, bool ganhou)
        => Assert.Equal((moedas, xp, ganhou), PegaFrutas.Recompensa(pontos));

    // ------------------------------------------------------------------ sons

    private static string RaizDoRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aurora.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Aurora.slnx não encontrado.");
    }

    [Fact]
    public void Sons_TodoSomEMusicaCitadosNoCodigoExistemComoArquivoWavValido()
    {
        string codigo = Path.Combine(RaizDoRepo(), "samples", "Aurora.Bichinhos");
        var citados = new HashSet<string>();
        // Pega a chamada inteira até o ";" e cada nome entre aspas dentro dela: cobre também
        // Som(venci ? "vitoria" : "derrota"), que um "Som(" colado na aspa deixaria passar.
        var chamada = new Regex(@"\b(?:Som|Musica)\(([^;]*);");
        var nomeEntreAspas = new Regex(@"""([a-z_]+)""");

        foreach (var arquivo in Directory.EnumerateFiles(codigo, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match c in chamada.Matches(File.ReadAllText(arquivo)))
            {
                foreach (Match n in nomeEntreAspas.Matches(c.Groups[1].Value))
                    citados.Add(n.Groups[1].Value);
            }
        }

        Assert.NotEmpty(citados);
        Assert.Contains("super", citados);     // vem de um ternário: prova que o regex vê além do 1º argumento
        Assert.Contains("derrota", citados);
        foreach (string nome in citados)
        {
            string caminho = Path.Combine(Assets, "sounds", nome + ".wav");
            Assert.True(File.Exists(caminho), $"falta sounds/{nome}.wav");

            // Cabeçalho RIFF/WAVE, PCM 16 bits mono: o que o leitor da engine aceita.
            byte[] bytes = File.ReadAllBytes(caminho);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(1, BitConverter.ToInt16(bytes, 20));    // PCM
            Assert.Equal(1, BitConverter.ToInt16(bytes, 22));    // mono
            Assert.Equal(16, BitConverter.ToInt16(bytes, 34));   // 16 bits
            Assert.True(bytes.Length > 1000, $"{nome}.wav vazio");
        }
    }
}
