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

    public IReadOnlyDictionary<string, int> Items => _items;

    public int GetCount(string item) => _items.TryGetValue(item, out int count) ? count : 0;

    public bool Has(string item, int count = 1) => GetCount(item) >= count;

    /// <summary>Soma (ou subtrai, com delta negativo) a quantidade do item. Nunca fica negativo;
    /// zera e remove a entrada em vez de guardar quantidade <= 0.</summary>
    public void Add(string item, int delta)
    {
        if (delta == 0)
            return;

        int newCount = Math.Max(0, GetCount(item) + delta);
        if (newCount == 0)
            _items.Remove(item);
        else
            _items[item] = newCount;

        Changed?.Invoke();
    }

    public void Remove(string item, int count) => Add(item, -count);

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
