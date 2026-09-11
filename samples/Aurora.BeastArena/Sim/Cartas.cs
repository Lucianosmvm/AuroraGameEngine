using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeastArena.Sim;

public enum TipoDeCarta
{
    /// <summary>Invoca uma ou mais unidades que andam e lutam sozinhas.</summary>
    Criatura,

    /// <summary>Efeito instantâneo (depois do <see cref="CartaDef.Atraso"/>) numa área.</summary>
    Feitico,
}

/// <summary>
/// Um estágio de evolução. Os multiplicadores valem sobre a ficha BASE da carta, não sobre o
/// estágio anterior — "Vida 2.3" no terceiro estágio é 2,3x a vida do lobo recém-invocado, o
/// que deixa a tabela legível sem ninguém precisar multiplicar em cadeia de cabeça.
/// </summary>
public sealed class EvolucaoDef
{
    public string Nome { get; set; } = "";

    /// <summary>XP TOTAL acumulado pra chegar neste estágio (não o que falta do anterior).</summary>
    public int Xp { get; set; }

    public float Vida { get; set; } = 1f;
    public float Dano { get; set; } = 1f;
    public float Velocidade { get; set; } = 1f;

    /// <summary>Cresce o corpo (raio de colisão) e o desenho. É o que faz a evolução ser
    /// visível do outro lado da tela sem ler número nenhum.</summary>
    public float Escala { get; set; } = 1f;

    /// <summary>Dano em área ganho no estágio. Null = mantém o da carta.</summary>
    public float? Area { get; set; }

    /// <summary>Mana que o OPONENTE ganha ao matar a unidade neste estágio. É o freio da bola
    /// de neve: deixar crescer rende, mas vira alvo valioso.</summary>
    public int Recompensa { get; set; }
}

/// <summary>A ficha de uma carta, lida de <c>Assets/database/cartas.json</c>.</summary>
public sealed class CartaDef
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";

    /// <summary>Letra desenhada no círculo enquanto não há arte.</summary>
    public string Letra { get; set; } = "?";

    public string Cor { get; set; } = "#FFFFFFFF";
    public TipoDeCarta Tipo { get; set; } = TipoDeCarta.Criatura;
    public int Custo { get; set; } = 3;

    /// <summary>Quantas unidades a carta invoca (enxame).</summary>
    public int Quantidade { get; set; } = 1;

    public float Vida { get; set; } = 100f;
    public float Dano { get; set; } = 10f;

    /// <summary>Segundos entre ataques.</summary>
    public float Cadencia { get; set; } = 1f;

    /// <summary>Distância de ataque, de borda a borda, em tiles.</summary>
    public float Alcance { get; set; } = 0.6f;

    /// <summary>Até onde a unidade enxerga inimigos pra desviar do caminho até a torre.</summary>
    public float Visao { get; set; } = 5.5f;

    /// <summary>Tiles por segundo.</summary>
    public float Velocidade { get; set; } = 1f;

    /// <summary>Criatura: raio do corpo. Feitiço: raio da área atingida.</summary>
    public float Raio { get; set; } = 0.45f;

    /// <summary>Peso na hora de empurrar e ser empurrado: o golem abre caminho, o morcego não.</summary>
    public float Massa { get; set; } = 1f;

    public bool Voa { get; set; }
    public bool AtacaAr { get; set; }

    /// <summary>No caminho ignora unidades e vai direto pro santuário; lá dentro, só bate em quem
    /// disputa o mesmo santuário (o tanque que segura a captura).</summary>
    public bool IgnoraUnidades { get; set; }

    /// <summary>Tiles por segundo do projétil. 0 = corpo a corpo, dano na hora.</summary>
    public float VelocidadeProjetil { get; set; }

    /// <summary>Raio do dano em área em volta do alvo. 0 = só o alvo.</summary>
    public float Area { get; set; }

    public float VenenoDps { get; set; }
    public float VenenoDuracao { get; set; }

    /// <summary>Multiplicador de velocidade aplicado no alvo (0.5 = metade). 1 = sem lentidão.</summary>
    public float Lentidao { get; set; } = 1f;

    public float LentidaoDuracao { get; set; }

    /// <summary>Feitiço: segundos entre soltar a carta e o efeito cair — a janela de reação de
    /// quem está embaixo dele.</summary>
    public float Atraso { get; set; }

    /// <summary>XP que a unidade dá a quem a mata. 0 = custo dividido pela quantidade.</summary>
    public int XpAoMorrer { get; set; }

    /// <summary>XP ganho quando um santuário vira da equipe com a unidade dentro. É o caminho de
    /// evolução de quem quase não mata ninguém (o rinoceronte).</summary>
    public int XpAoCapturar { get; set; }

    public List<EvolucaoDef> Evolucoes { get; set; } = [];

    public int XpQueVale => XpAoMorrer > 0
        ? XpAoMorrer
        : Math.Max(1, (int)MathF.Round(Custo / (float)Math.Max(1, Quantidade)));
}

public sealed class CatalogoCartas
{
    public const string Caminho = "database/cartas.json";
    public const int TamanhoDoBaralho = 8;

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class Arquivo
    {
        public List<CartaDef> Cartas { get; set; } = [];
        public List<string> Baralho { get; set; } = [];
    }

    public IReadOnlyList<CartaDef> Todas { get; private init; } = [];

    /// <summary>Ids do baralho padrão, na ordem do arquivo (a batalha embaralha).</summary>
    public IReadOnlyList<string> IdsDoBaralho { get; private init; } = [];

    public CartaDef Get(string id)
        => Todas.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException($"Carta '{id}' não existe em {Caminho}.");

    public IReadOnlyList<CartaDef> Baralho() => IdsDoBaralho.Select(Get).ToList();

    /// <summary>
    /// Lê e CONFERE o catálogo. Erro de dado vira exceção com o nome da carta já no boot: uma
    /// evolução com XP fora de ordem nunca dispararia, e ninguém descobriria jogando.
    /// </summary>
    public static CatalogoCartas Carregar(string json)
    {
        var arquivo = JsonSerializer.Deserialize<Arquivo>(json, Opcoes)
            ?? throw new InvalidOperationException($"{Caminho} vazio ou inválido.");

        var catalogo = new CatalogoCartas { Todas = arquivo.Cartas, IdsDoBaralho = arquivo.Baralho };

        if (catalogo.IdsDoBaralho.Count != TamanhoDoBaralho)
            throw new InvalidOperationException(
                $"{Caminho}: o baralho precisa de {TamanhoDoBaralho} cartas, tem {catalogo.IdsDoBaralho.Count}.");

        foreach (var carta in catalogo.Todas)
        {
            int anterior = 0;
            foreach (var evolucao in carta.Evolucoes)
            {
                if (evolucao.Xp <= anterior)
                    throw new InvalidOperationException(
                        $"{Caminho}: '{carta.Id}' tem evolução '{evolucao.Nome}' com Xp {evolucao.Xp}, " +
                        $"que precisa ser maior que o estágio anterior ({anterior}).");
                anterior = evolucao.Xp;
            }
        }

        _ = catalogo.Baralho();   // id do baralho que não existe estoura aqui, não no meio da partida
        return catalogo;
    }
}
