using System.Text.Json;

// Ver a nota em Partida.cs: no Android existe Android.App.GameState, e o apelido é o que
// mantém o mesmo arquivo compilando nas duas plataformas.
using GameState = Aurora.Runtime.GameState;

namespace FruitNinja;

/// <summary>
/// A ficha de uma lâmina — a "arma" deste jogo. Ela não é uma entidade na cena: é o conjunto
/// de números que muda como o corte se comporta e como o rastro aparece.
///
/// <para>Cadastrar arma nova = uma entrada em <c>Assets/database/laminas.json</c>. Nenhum
/// script cita lâmina por nome; o <see cref="Lamina"/> lê a equipada e usa os campos daqui.</para>
/// </summary>
public sealed class LaminaDef
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";

    /// <summary>Cor do rastro (#RRGGBBAA). O rastro é desenhado em duas passadas: um traço
    /// grosso nesta cor e um miolo fino em <see cref="CorMiolo"/>.</summary>
    public string Cor { get; set; } = "#8AD8FFCC";

    public string CorMiolo { get; set; } = "#FFFFFFFF";

    /// <summary>Espessura do rastro em pixels de mundo, na base do traço.</summary>
    public float Espessura { get; set; } = 18f;

    /// <summary>Segundos que cada ponto do rastro leva pra sumir. Rastro longo é mais vistoso,
    /// e também mostra melhor o desenho do combo.</summary>
    public float Duracao { get; set; } = 0.22f;

    /// <summary>Multiplicador do raio de acerto. 1.3 = lâmina "generosa", que corta de
    /// raspão — é a alavanca principal de melhoria da loja.</summary>
    public float Alcance { get; set; } = 1f;

    /// <summary>Multiplica os pontos de cada fruta cortada com ela.</summary>
    public float MultiplicadorPontos { get; set; } = 1f;

    /// <summary>Velocidade mínima do dedo, em pixels/s, pra o traço cortar. Encostar parado não
    /// corta em nenhuma lâmina; uma lâmina pesada exige o gesto mais rápido.</summary>
    public float VelocidadeMinima { get; set; } = 260f;

    /// <summary>Preço em moedas. 0 = já vem com o jogador.</summary>
    public int Preco { get; set; }
}

/// <summary>
/// Catálogo de lâminas lido de <c>Assets/database/laminas.json</c>, mais a memória de qual
/// está comprada e equipada.
///
/// <para>Compra e escolha vivem no <c>GameState</c> como switches (<c>Lamina_katana</c>,
/// <c>Equipada_katana</c>) em vez de números: switch é gravado no save de graça e, por ser
/// nomeado pelo id, continua certo mesmo se você reordenar o JSON depois.</para>
/// </summary>
public sealed class CatalogoLaminas
{
    public const string Caminho = "database/laminas.json";

    public static CatalogoLaminas Atual { get; set; } = new();

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class Arquivo
    {
        public List<LaminaDef> Laminas { get; set; } = [];
    }

    public List<LaminaDef> Todas { get; private set; } =
        [new LaminaDef { Id = "padrao", Nome = "Lâmina" }];

    public static CatalogoLaminas Carregar(string json)
    {
        var arquivo = JsonSerializer.Deserialize<Arquivo>(json, Opcoes)
            ?? throw new InvalidOperationException("laminas.json vazio ou inválido.");

        if (arquivo.Laminas.Count == 0)
            throw new InvalidOperationException("laminas.json precisa de pelo menos uma lâmina.");

        return new CatalogoLaminas { Todas = arquivo.Laminas };
    }

    public static string ChaveCompra(LaminaDef lamina) => $"Lamina_{lamina.Id}";

    public static string ChaveEquipada(LaminaDef lamina) => $"Equipada_{lamina.Id}";

    /// <summary>Lâmina de graça é considerada comprada — senão o jogador começaria sem arma
    /// nenhuma e o rastro não apareceria.</summary>
    public bool Comprada(GameState estado, LaminaDef lamina)
        => lamina.Preco <= 0 || estado.GetSwitch(ChaveCompra(lamina));

    public bool Comprar(GameState estado, LaminaDef lamina, out string mensagem)
    {
        if (Comprada(estado, lamina))
        {
            mensagem = $"{lamina.Nome} já é sua.";
            return false;
        }

        float moedas = estado.GetVariable(Partida.VarMoedas);
        if (moedas < lamina.Preco)
        {
            mensagem = $"Faltam {lamina.Preco - (int)moedas} moedas.";
            return false;
        }

        estado.AddVariable(Partida.VarMoedas, -lamina.Preco);
        estado.SetSwitch(ChaveCompra(lamina), true);
        Equipar(estado, lamina);
        mensagem = $"{lamina.Nome} comprada e equipada!";
        return true;
    }

    public void Equipar(GameState estado, LaminaDef lamina)
    {
        foreach (var outra in Todas)
            estado.SetSwitch(ChaveEquipada(outra), false);

        estado.SetSwitch(ChaveEquipada(lamina), true);
    }

    /// <summary>A lâmina em uso. Cai na primeira do catálogo quando não há nenhuma marcada —
    /// primeira partida, save antigo, ou alguém apagando do JSON a que estava equipada.</summary>
    public LaminaDef Equipada(GameState estado)
    {
        foreach (var lamina in Todas)
        {
            if (estado.GetSwitch(ChaveEquipada(lamina)) && Comprada(estado, lamina))
                return lamina;
        }

        return Todas[0];
    }
}
