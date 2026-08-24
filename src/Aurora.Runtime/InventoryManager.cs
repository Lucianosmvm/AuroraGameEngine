namespace Aurora.Runtime;

/// <summary>
/// Itens do jogador: nome → quantidade. Eventos leem/escrevem aqui (ações AddItem/RemoveItem,
/// gatilho HasItem); o sistema de save persiste junto com <see cref="GameState"/>.
/// </summary>
public sealed class InventoryManager
{
    private readonly Dictionary<string, int> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Disparado em qualquer mudança (HUD de inventário reagir).</summary>
    public event Action? Changed;

    /// <summary>
    /// Catálogo consultado pra respeitar o <see cref="Database.ItemDefinition.MaxStack"/>. Null
    /// (padrão) = sem teto nenhum, que é o comportamento de quem usa o inventário sem banco.
    ///
    /// <para>Antes o campo MaxStack existia no banco e no editor mas ninguém o lia: dava pra
    /// cadastrar "máximo 10 poções" e carregar 300. Ou o teto vale, ou o campo é mentira.</para>
    /// </summary>
    public Database.ItemDatabase? Database { get; set; }

    public IReadOnlyDictionary<string, int> Items => _items;

    public int GetCount(string item) => _items.TryGetValue(item, out int count) ? count : 0;

    public bool Has(string item, int count = 1) => GetCount(item) >= count;

    /// <summary>
    /// Soma (ou subtrai, com delta negativo) a quantidade do item. Nunca fica negativo; zera e
    /// remove a entrada em vez de guardar quantidade &lt;= 0. Ao somar, respeita o MaxStack da
    /// ficha do item (quando há <see cref="Database"/>).
    /// </summary>
    /// <returns>Quanto entrou (ou saiu, negativo) de fato. Menos que o pedido quando o teto do
    /// item cortou, ou quando não havia tudo aquilo pra tirar — é o que uma loja precisa saber
    /// pra não cobrar por item que não coube.</returns>
    public int Add(string item, int delta)
    {
        if (delta == 0)
            return 0;

        int current = GetCount(item);
        int newCount = Math.Max(0, current + delta);

        if (delta > 0 && Database?.Get(item) is { MaxStack: > 0 } definition)
            newCount = Math.Min(newCount, Math.Max(current, definition.MaxStack));

        if (newCount == current)
            return 0;

        if (newCount == 0)
            _items.Remove(item);
        else
            _items[item] = newCount;

        Changed?.Invoke();
        return newCount - current;
    }

    public int Remove(string item, int count) => Add(item, -count);

    public void Clear()
    {
        _items.Clear();
        Changed?.Invoke();
    }

    internal void LoadFromDictionary(IReadOnlyDictionary<string, int> items)
    {
        _items.Clear();
        foreach (var (key, value) in items)
            _items[key] = value;
        Changed?.Invoke();
    }
}
