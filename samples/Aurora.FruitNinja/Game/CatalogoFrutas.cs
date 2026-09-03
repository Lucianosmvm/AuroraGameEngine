using System.Text.Json;
using System.Text.Json.Serialization;

namespace FruitNinja;

/// <summary>O que uma coisa lançada É. Fruta comum, bomba ou banana de poder — todas passam
/// pela mesma ficha, só mudando os campos.</summary>
public enum TipoDeFruta
{
    Fruta,
    Bomba,
    Poder,
}

/// <summary>Efeito de uma banana especial, no espírito do Fruit Ninja original.</summary>
public enum EfeitoDePoder
{
    Nenhum,

    /// <summary>Congela: tudo cai em câmera lenta por alguns segundos.</summary>
    Congelar,

    /// <summary>Frenesi: chuva de frutas, sem bomba nenhuma, enquanto durar.</summary>
    Frenesi,

    /// <summary>Pontos em dobro enquanto durar.</summary>
    Dobro,
}

/// <summary>
/// A ficha de uma fruta: tudo que diferencia melancia de bomba mora aqui, como DADO.
///
/// <para>É este arquivo que faz o "quero mais frutas depois" custar uma entrada de JSON e dois
/// PNGs, sem recompilar nada: <c>Assets/database/frutas.json</c> lista as fichas, e nenhum
/// script cita fruta por nome.</para>
/// </summary>
public sealed class FrutaDef
{
    /// <summary>Identificador único. É por ele que a arte, o save e as mensagens se referem
    /// à fruta.</summary>
    public string Id { get; set; } = "";

    /// <summary>Nome que aparece na tela (aviso de poder, tela de fim).</summary>
    public string Nome { get; set; } = "";

    /// <summary>Textura da fruta inteira, relativa a Assets.</summary>
    public string Sprite { get; set; } = "";

    /// <summary>Textura de UMA metade — a esquerda. A outra metade é a mesma imagem espelhada
    /// (FlipX), então cada fruta precisa de dois arquivos, não três. Vazio = a fruta não se
    /// parte (bomba).</summary>
    public string SpriteMetade { get; set; } = "";

    public TipoDeFruta Tipo { get; set; } = TipoDeFruta.Fruta;

    /// <summary>Só vale quando <see cref="Tipo"/> é Poder.</summary>
    public EfeitoDePoder Efeito { get; set; } = EfeitoDePoder.Nenhum;

    /// <summary>Segundos que o efeito do poder dura.</summary>
    public float DuracaoEfeito { get; set; } = 5f;

    /// <summary>Pontos por cortar. O combo e o poder "Dobro" multiplicam isto.</summary>
    public int Pontos { get; set; } = 1;

    /// <summary>Moedas que caem ao cortar — é o que se gasta na loja de lâminas.</summary>
    public int Moedas { get; set; } = 1;

    /// <summary>Peso no sorteio: uma fruta com peso 20 sai o dobro das vezes de uma com 10.
    /// Peso 0 tira a fruta do jogo sem apagar a ficha.</summary>
    public float Peso { get; set; } = 10f;

    /// <summary>Só entra no sorteio a partir deste nível — é como fruta nova vai sendo
    /// apresentada conforme a partida avança.</summary>
    public int NivelMinimo { get; set; } = 1;

    /// <summary>Para de sair a partir deste nível. 0 = nunca sai de cartaz.</summary>
    public int NivelMaximo { get; set; }

    /// <summary>Diâmetro desenhado, em pixels de mundo.</summary>
    public float Tamanho { get; set; } = 104f;

    /// <summary>Multiplicador do raio de acerto sobre o tamanho. Menor que 1 = fruta difícil de
    /// acertar (exige mira), maior = perdoa passar de raspão.</summary>
    public float RaioCorte { get; set; } = 0.5f;

    /// <summary>Cor das gotas que espirram ao cortar, em #RRGGBBAA.</summary>
    public string CorSuco { get; set; } = "#FFFFFFFF";

    /// <summary>Deixar escapar custa uma vida. Bomba e poder não custam — no original, deixar
    /// a bomba passar é justamente o que se quer.</summary>
    public bool PerdeVidaSeEscapar { get; set; } = true;

    /// <summary>Cortar isto acaba a partida na hora (bomba).</summary>
    public bool MataAoCortar { get; set; }

    /// <summary>Vidas ganhas ao cortar. Serve pra criar uma fruta-coração depois: basta uma
    /// ficha com <c>"VidasAoCortar": 1</c>.</summary>
    public int VidasAoCortar { get; set; }

    /// <summary>Quantos pedaços de gota espirram. Fruta suculenta pode espirrar mais.</summary>
    public int Gotas { get; set; } = 14;
}

/// <summary>
/// O catálogo lido de <c>Assets/database/frutas.json</c> e o sorteio de quem é lançado.
///
/// <para><see cref="Atual"/> é estático porque quem sorteia é um <c>Behavior</c> na cena
/// (<see cref="Lancador"/>), e Behavior só recebe o <c>World</c> — não tem por onde receber o
/// catálogo. O <see cref="NinjaGame"/> carrega uma vez no boot e publica aqui.</para>
/// </summary>
public sealed class CatalogoFrutas
{
    public const string Caminho = "database/frutas.json";

    /// <summary>Catálogo em uso. Nunca é null depois do boot do <see cref="NinjaGame"/>.</summary>
    public static CatalogoFrutas Atual { get; set; } = new();

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class Arquivo
    {
        public List<FrutaDef> Frutas { get; set; } = [];
    }

    public List<FrutaDef> Todas { get; private set; } = [];

    public FrutaDef? Get(string id)
        => Todas.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    public static CatalogoFrutas Carregar(string json)
    {
        var arquivo = JsonSerializer.Deserialize<Arquivo>(json, Opcoes)
            ?? throw new InvalidOperationException("frutas.json vazio ou inválido.");

        return new CatalogoFrutas { Todas = arquivo.Frutas };
    }

    /// <summary>Fichas elegíveis num nível — o filtro que faz fruta nova estrear sozinha.</summary>
    public IEnumerable<FrutaDef> Disponiveis(int nivel, TipoDeFruta tipo)
        => Todas.Where(f => f.Tipo == tipo && f.Peso > 0f
            && nivel >= f.NivelMinimo && (f.NivelMaximo <= 0 || nivel <= f.NivelMaximo));

    /// <summary>Sorteia uma ficha do tipo pedido, por peso. Null se não há nenhuma elegível —
    /// acontece de verdade (nível 1 sem bomba cadastrada) e não é erro.</summary>
    public FrutaDef? Sortear(int nivel, TipoDeFruta tipo, Random rng)
    {
        float total = 0f;
        foreach (var f in Disponiveis(nivel, tipo))
            total += f.Peso;

        if (total <= 0f)
            return null;

        float alvo = (float)rng.NextDouble() * total;
        foreach (var f in Disponiveis(nivel, tipo))
        {
            alvo -= f.Peso;
            if (alvo <= 0f)
                return f;
        }

        return Disponiveis(nivel, tipo).LastOrDefault();
    }
}
