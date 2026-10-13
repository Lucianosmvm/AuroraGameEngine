using System.Text.Json;

namespace Bichinhos;

/// <param name="X">Centro, em % da largura do sprite (0 a 100).</param>
/// <param name="Y">Em % da altura do sprite, de cima para baixo.</param>
/// <param name="Largura">Em % da largura do sprite.</param>
public readonly record struct Ancora(float X, float Y, float Largura);

/// <summary>Onde os itens encaixam numa forma do bichinho. Óculos não têm: usam os olhos.</summary>
public sealed record FormaAncoras(Ancora Cabeca, Ancora Pescoco);

/// <param name="Escala">Largura do item = largura da âncora x escala.</param>
/// <param name="Dx">Deslocamento lateral, em larguras da âncora (o laço fica de lado).</param>
/// <param name="Rot">Giro, em radianos (horário).</param>
public sealed record ConfigItem(float Escala, float Dx, float Rot);

/// <summary>
/// Os pontos de encaixe, lidos de <c>database/ancoras.json</c>. O arquivo é GERADO por
/// <c>Arte/gerar_ancoras.py</c> olhando a silhueta de cada sprite: se um sprite mudar, rode o
/// script de novo em vez de mexer aqui.
/// </summary>
public sealed class Ancoras
{
    public const string Caminho = "database/ancoras.json";

    public static Ancoras Atual { get; set; } = new();

    public Dictionary<string, FormaAncoras> Formas { get; } = new();
    public Dictionary<string, ConfigItem> Itens { get; } = new();

    /// <summary>Âncoras de uma forma, ou null se o JSON não a conhece (o item simplesmente não aparece).</summary>
    public FormaAncoras? Forma(string especieId, int estagio)
        => Formas.TryGetValue($"{especieId}_{estagio}", out var f) ? f : null;

    public ConfigItem Config(string itemId)
        => Itens.TryGetValue(itemId, out var c) ? c : new ConfigItem(1f, 0f, 0f);

    public static Ancoras Carregar(string json)
    {
        var resultado = new Ancoras();
        using var doc = JsonDocument.Parse(json);

        foreach (var forma in doc.RootElement.GetProperty("Formas").EnumerateObject())
        {
            resultado.Formas[forma.Name] = new FormaAncoras(
                Ler(forma.Value.GetProperty("Cabeca")),
                Ler(forma.Value.GetProperty("Pescoco")));
        }

        foreach (var item in doc.RootElement.GetProperty("Itens").EnumerateObject())
        {
            resultado.Itens[item.Name] = new ConfigItem(
                item.Value.GetProperty("escala").GetSingle(),
                item.Value.GetProperty("dx").GetSingle(),
                item.Value.GetProperty("rot").GetSingle());
        }

        return resultado;
    }

    private static Ancora Ler(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
}
