using System.Text.Json;

namespace Aurora.Runtime.Database;

/// <summary>
/// Textos fixos do jogo num lugar só — o "Terms" do RPG Maker.
///
/// <para>Serve pra duas coisas: trocar a palavra que a engine mostra ("Comprar" virar "Trocar"
/// num jogo de escambo, "Ouro" virar "Créditos" numa nave) e traduzir o jogo sem caçar string
/// dentro de código. Todo texto que a engine escreve na tela passa por aqui com um padrão em
/// português embutido — sem o arquivo, tudo continua como está.</para>
///
/// <para>Texto de diálogo, nome de item e descrição NÃO moram aqui: aquilo é conteúdo do jogo, e
/// já tem lugar próprio (a cena e o banco de itens). Aqui ficam só as palavras da interface.</para>
/// </summary>
public sealed class TermDatabase
{
    private readonly Dictionary<string, string> _terms = new(StringComparer.OrdinalIgnoreCase);

    public const string DefaultPath = "database/terms.json";

    public IReadOnlyDictionary<string, string> Terms => _terms;

    public int Count => _terms.Count;

    /// <summary>
    /// Chaves que a engine consulta sozinha, com o texto padrão de cada uma. É a lista que o
    /// editor oferece — sem ela, descobrir que "shop.cantAfford" existe exigiria ler o código.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Default, string Description)> KnownKeys =
    [
        ("shop.buy",        "Comprar",                              "Opção de comprar, no balcão da loja"),
        ("shop.sell",       "Vender",                               "Opção de vender, no balcão da loja"),
        ("shop.exit",       "Sair",                                 "Opção que fecha a loja"),
        ("shop.empty",      "Não tenho nada pra vender hoje.",      "Loja aberta sem nenhuma mercadoria válida"),
        ("shop.nothingToSell", "Você não tem nada que eu queira comprar.", "Jogador sem item vendável"),
        ("shop.cantAfford", "Não dá pro seu bolso.",                "Dinheiro insuficiente"),
        ("shop.full",       "Você já carrega o quanto pode disso.", "Item no limite de pilha"),
    ];

    /// <summary>Texto da chave, ou <paramref name="fallback"/> quando ela não foi cadastrada.</summary>
    public string Get(string key, string fallback)
        => _terms.TryGetValue(key, out var text) && text.Length > 0 ? text : fallback;

    /// <summary>Texto da chave, caindo no padrão de <see cref="KnownKeys"/> e, se nem isso existir,
    /// na própria chave — é o que o token <c>{Term:…}</c> do UiText usa.</summary>
    public string Get(string key)
    {
        if (_terms.TryGetValue(key, out var text) && text.Length > 0)
            return text;

        foreach (var (known, standard, _) in KnownKeys)
        {
            if (known.Equals(key, StringComparison.OrdinalIgnoreCase))
                return standard;
        }

        return key;
    }

    public void Set(string key, string text) => _terms[key] = text;

    public void Clear() => _terms.Clear();

    /// <summary>
    /// Lê <c>{ "Terms": { "shop.buy": "Comprar" } }</c>. Aceita também a forma em lista
    /// (<c>[{ "Key": …, "Text": … }]</c>), que é a que o editor grava por ser mais fácil de editar
    /// linha a linha.
    /// </summary>
    public void Load(string json)
    {
        _terms.Clear();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Terms", out var terms))
            return;

        if (terms.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in terms.EnumerateObject())
            {
                if (property.Value.GetString() is { } text)
                    _terms[property.Name] = text;
            }
            return;
        }

        if (terms.ValueKind != JsonValueKind.Array)
            return;

        foreach (var element in terms.EnumerateArray())
        {
            string key = element.TryGetProperty("Key", out var k) ? k.GetString() ?? "" : "";
            if (key.Length == 0)
                continue;

            _terms[key] = element.TryGetProperty("Text", out var t) ? t.GetString() ?? "" : "";
        }
    }
}
