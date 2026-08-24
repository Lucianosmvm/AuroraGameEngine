using System.Text.Json;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Database;

/// <summary>Uma possibilidade dentro de uma <see cref="SpawnTable"/>.</summary>
public sealed class SpawnEntry
{
    /// <summary>Prefab instanciado se esta entrada for sorteada.</summary>
    public string Prefab = "";

    /// <summary>Peso relativo no sorteio. Peso 3 contra peso 1 sai três vezes mais. 0 ou negativo
    /// nunca sai — serve pra desligar uma entrada sem apagá-la.</summary>
    public float Weight = 1f;

    /// <summary>
    /// Condição opcional pra entrada entrar no sorteio, no MESMO formato da ação If dos eventos
    /// (<c>Text</c> = Variable/Switch/Item/Quest, <c>Name</c>, <c>Op</c>, <c>Value</c>, <c>On</c>).
    ///
    /// <para>É o que faz "zumbi só à noite" e "chefe só depois da fase 3" caberem na tabela, em
    /// vez de exigirem um spawner separado por caso. Null = sempre elegível.</para>
    /// </summary>
    public EventAction? Condition;
}

/// <summary>
/// Um grupo nomeado de prefabs sorteados por peso. É o que permite dizer "nasce um inimigo da
/// floresta" em vez de "nasce prefabs/slime.json" — a cena refere o grupo pelo id, e o que existe
/// dentro dele muda sem tocar em nenhuma cena.
/// </summary>
public sealed class SpawnTable
{
    public string Id = "";
    public List<SpawnEntry> Entries = [];

    /// <summary>
    /// Sorteia um prefab entre as entradas elegíveis. Devolve null quando nenhuma passa na
    /// condição — nesse caso não nasce nada, que é o comportamento certo pra "de dia não tem
    /// zumbi" (o contrário, cair na primeira entrada, faria zumbi nascer de dia).
    /// </summary>
    public string? Pick(Func<EventAction, bool> conditionTest, Random random)
    {
        float total = 0f;
        foreach (var entry in Entries)
        {
            if (entry.Weight > 0f && entry.Prefab.Length > 0 && IsEligible(entry, conditionTest))
                total += entry.Weight;
        }

        if (total <= 0f)
            return null;

        // Roleta: caminha os pesos acumulados até passar do ponto sorteado. Sortear um índice
        // direto ignoraria o peso, e é justamente o peso que deixa o chefe raro e o slime comum.
        float roll = (float)random.NextDouble() * total;

        foreach (var entry in Entries)
        {
            if (entry.Weight <= 0f || entry.Prefab.Length == 0 || !IsEligible(entry, conditionTest))
                continue;

            roll -= entry.Weight;
            if (roll <= 0f)
                return entry.Prefab;
        }

        return null;
    }

    private static bool IsEligible(SpawnEntry entry, Func<EventAction, bool> conditionTest)
        => entry.Condition is null || conditionTest(entry.Condition);
}

/// <summary>
/// Catálogo de tabelas de spawn, carregado de <c>database/spawns.json</c>. Junto com o
/// <see cref="ItemDatabase"/>, é a outra metade do "banco de dados" no sentido do RPG Maker.
///
/// <para>Onde a engine espera um prefab (ação <c>Spawn</c>, componente <c>Spawner</c>,
/// <c>AttackSpawner</c>), pode-se escrever o id de uma tabela: quem resolve é
/// <see cref="Resolve"/>, então todos ganham sorteio e condição de uma vez, sem cada um saber
/// que tabelas existem.</para>
/// </summary>
public sealed class SpawnTableDatabase
{
    private readonly Dictionary<string, SpawnTable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();

    public const string DefaultPath = "database/spawns.json";

    public IReadOnlyDictionary<string, SpawnTable> Tables => _tables;

    public int Count => _tables.Count;

    public SpawnTable? Get(string id) => _tables.TryGetValue(id, out var table) ? table : null;

    public void Add(SpawnTable table) => _tables[table.Id] = table;

    public void Clear() => _tables.Clear();

    /// <summary>
    /// Traduz o que a cena escreveu num caminho de prefab concreto.
    ///
    /// <para>Se <paramref name="nameOrPath"/> for id de tabela, sorteia. Se não for, devolve o
    /// próprio texto — assim continuar escrevendo o caminho direto segue funcionando, e nenhuma
    /// cena antiga quebra. Null só quando a tabela existe mas nada passou na condição.</para>
    /// </summary>
    public string? Resolve(string nameOrPath, Func<EventAction, bool> conditionTest)
        => Get(nameOrPath) is { } table ? table.Pick(conditionTest, _random) : nameOrPath;

    /// <summary>Lê <c>{ "Tables": [ { "Id": …, "Entries": [ … ] } ] }</c>, substituindo o conteúdo.</summary>
    public void Load(string json)
    {
        _tables.Clear();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Tables", out var tables))
            return;

        foreach (var element in tables.EnumerateArray())
        {
            string id = element.TryGetProperty("Id", out var idProp) ? idProp.GetString() ?? "" : "";
            if (id.Length == 0)
            {
                Console.Error.WriteLine("[SpawnTableDatabase] Tabela sem \"Id\" — ignorada.");
                continue;
            }

            var table = new SpawnTable { Id = id };

            if (element.TryGetProperty("Entries", out var entries))
            {
                foreach (var entryElement in entries.EnumerateArray())
                {
                    var entry = new SpawnEntry
                    {
                        Prefab = entryElement.TryGetProperty("Prefab", out var p) ? p.GetString() ?? "" : "",
                        Weight = entryElement.TryGetProperty("Weight", out var w) ? w.GetSingle() : 1f,
                    };

                    if (entryElement.TryGetProperty("Condition", out var condition))
                        entry.Condition = ParseCondition(condition);

                    table.Entries.Add(entry);
                }
            }

            _tables[id] = table;
        }
    }

    /// <summary>Uma condição é uma ação If solta — reusa o parser da lista de ações pra não haver
    /// dois formatos de condição no projeto.</summary>
    private static EventAction? ParseCondition(JsonElement element)
    {
        // ParseList espera um array; embrulhar é mais barato que duplicar o parser de um objeto.
        using var wrapper = JsonDocument.Parse($"[{element.GetRawText()}]");
        var list = EventAction.ParseList(wrapper.RootElement);
        return list.Count > 0 ? list[0] : null;
    }
}
