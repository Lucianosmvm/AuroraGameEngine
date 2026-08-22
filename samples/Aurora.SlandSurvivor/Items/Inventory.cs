namespace Aurora.SlandSurvivor.Items;

/// <summary>
/// Mochila do jogador: 40 espaços, sendo os 10 primeiros a barra rápida (o que a mão segura).
/// Empilha por item respeitando <see cref="ItemDef.MaxStack"/> e sempre preenche primeiro a
/// barra rápida — assim o que você acabou de minerar já fica pronto para colocar de volta.
/// </summary>
public sealed class Inventory
{
    public const int HotbarSize = 10;
    public const int TotalSlots = 40;

    public struct Slot
    {
        public int Item;
        public int Count;

        public readonly bool IsEmpty => Item < 0 || Count <= 0;
    }

    public readonly Slot[] Slots = new Slot[TotalSlots];

    private int _selected;

    public Inventory()
    {
        for (int i = 0; i < Slots.Length; i++)
            Slots[i] = new Slot { Item = ItemIds.None, Count = 0 };
    }

    /// <summary>Índice do espaço da barra rápida em uso (0–9).</summary>
    public int Selected
    {
        get => _selected;
        set => _selected = (value % HotbarSize + HotbarSize) % HotbarSize;
    }

    public int SelectedItem => Slots[_selected].IsEmpty ? ItemIds.None : Slots[_selected].Item;

    /// <summary>Guarda o item e devolve quanto sobrou (0 = coube tudo).</summary>
    public int Add(int item, int count = 1)
    {
        if (item < 0 || count <= 0 || ItemDb.Get(item) is not { } def)
            return count;

        // 1) completa pilhas existentes; 2) ocupa espaços vazios. Nas duas passadas a barra
        // rápida vem primeiro porque os índices dela são os menores.
        for (int pass = 0; pass < 2 && count > 0; pass++)
        {
            for (int i = 0; i < Slots.Length && count > 0; i++)
            {
                ref var slot = ref Slots[i];

                if (pass == 0)
                {
                    if (slot.IsEmpty || slot.Item != item || slot.Count >= def.MaxStack)
                        continue;
                }
                else if (!slot.IsEmpty)
                {
                    continue;
                }

                slot.Item = item;
                int space = def.MaxStack - slot.Count;
                int moved = Math.Min(space, count);
                slot.Count += moved;
                count -= moved;
            }
        }

        return count;
    }

    public int CountOf(int item)
    {
        int total = 0;
        foreach (var slot in Slots)
        {
            if (!slot.IsEmpty && slot.Item == item)
                total += slot.Count;
        }

        return total;
    }

    public bool Has(int item, int count) => CountOf(item) >= count;

    /// <summary>Gasta <paramref name="count"/> unidades. Só mexe no inventário se houver o total.</summary>
    public bool Consume(int item, int count = 1)
    {
        if (!Has(item, count))
            return false;

        for (int i = Slots.Length - 1; i >= 0 && count > 0; i--)
        {
            ref var slot = ref Slots[i];
            if (slot.IsEmpty || slot.Item != item)
                continue;

            int taken = Math.Min(slot.Count, count);
            slot.Count -= taken;
            count -= taken;

            if (slot.Count <= 0)
                slot = new Slot { Item = ItemIds.None, Count = 0 };
        }

        return true;
    }

    /// <summary>Gasta uma unidade do espaço em uso (colocar bloco, beber poção).</summary>
    public bool ConsumeSelected()
    {
        ref var slot = ref Slots[_selected];
        if (slot.IsEmpty)
            return false;

        slot.Count--;
        if (slot.Count <= 0)
            slot = new Slot { Item = ItemIds.None, Count = 0 };

        return true;
    }

    /// <summary>Troca dois espaços (clique no inventário manda o item para a barra rápida).</summary>
    public void Swap(int a, int b)
    {
        if (a < 0 || b < 0 || a >= Slots.Length || b >= Slots.Length || a == b)
            return;

        (Slots[a], Slots[b]) = (Slots[b], Slots[a]);
    }

    /// <summary>Maior poder de picareta guardado — usado só para mensagens de "precisa de X".</summary>
    public int BestPickaxePower()
    {
        int best = ItemDb.BasePower;
        foreach (var slot in Slots)
        {
            if (!slot.IsEmpty)
                best = Math.Max(best, ItemDb.Get(slot.Item)?.Power ?? 0);
        }

        return best;
    }

    public void Clear()
    {
        for (int i = 0; i < Slots.Length; i++)
            Slots[i] = new Slot { Item = ItemIds.None, Count = 0 };
    }
}
