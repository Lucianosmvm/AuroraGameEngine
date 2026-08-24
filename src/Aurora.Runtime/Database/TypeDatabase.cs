using System.Text.Json;

namespace Aurora.Runtime.Database;

/// <summary>
/// Listas de categorias do jogo — o "Types" do RPG Maker, mas sem categoria embutida nenhuma.
///
/// <para>O problema que isto resolve é bobo e caro: campo de categoria escrito à mão vira
/// "Consumivel" numa ficha e "Consumível" na outra, e nada avisa — a loja filtra errado, a HUD
/// separa em duas abas, e o autor só descobre jogando. Cadastrando a lista, o editor sugere os
/// valores que já existem e o jogo avisa no boot quando alguém escreveu fora dela.</para>
///
/// <para>Quais listas existem é decisão do jogo: <c>ItemTypes</c> é a única que a engine consulta
/// sozinha (pra conferir o campo Type dos itens). Um jogo de corrida pode cadastrar
/// <c>CategoriasDePista</c> e ler pelos próprios scripts; um jogo sem categoria nenhuma
/// simplesmente não cria o arquivo.</para>
/// </summary>
public sealed class TypeDatabase
{
    private readonly Dictionary<string, List<string>> _lists = new(StringComparer.OrdinalIgnoreCase);

    public const string DefaultPath = "database/types.json";

    /// <summary>Lista que a engine confere sozinha: os valores válidos do campo Type do item.</summary>
    public const string ItemTypes = "ItemTypes";

    public IReadOnlyDictionary<string, List<string>> Lists => _lists;

    public int Count => _lists.Count;

    /// <summary>Valores de uma lista. Lista inexistente devolve vazio — nunca null, porque quem
    /// chama quase sempre quer só varrer.</summary>
    public IReadOnlyList<string> Get(string listId)
        => _lists.TryGetValue(listId, out var values) ? values : [];

    /// <summary>
    /// O valor pertence à lista? Ignora maiúsculas. <b>Lista vazia ou inexistente devolve true</b>:
    /// não cadastrar a lista significa "não quero controle aqui", e nesse caso qualquer texto vale.
    /// </summary>
    public bool Contains(string listId, string value)
    {
        if (value.Length == 0)
            return true;

        var values = Get(listId);
        if (values.Count == 0)
            return true;

        foreach (string candidate in values)
        {
            if (candidate.Equals(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void Clear() => _lists.Clear();

    /// <summary>
    /// Lê <c>{ "Types": [ { "Id": "ItemTypes", "Values": ["Consumivel", "Arma"] } ] }</c>.
    /// </summary>
    public void Load(string json)
    {
        _lists.Clear();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Types", out var types))
            return;

        foreach (var element in types.EnumerateArray())
        {
            string id = element.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "" : "";
            if (id.Length == 0)
            {
                Console.Error.WriteLine("[TypeDatabase] Lista sem \"Id\" — ignorada.");
                continue;
            }

            var values = new List<string>();
            if (element.TryGetProperty("Values", out var array))
            {
                foreach (var value in array.EnumerateArray())
                {
                    if (value.GetString() is { Length: > 0 } text)
                        values.Add(text);
                }
            }

            _lists[id] = values;
        }
    }
}
