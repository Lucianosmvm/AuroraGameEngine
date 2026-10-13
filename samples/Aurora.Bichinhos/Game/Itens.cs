namespace Bichinhos;

/// <summary>Onde o item fica no corpo. Um item por lugar: chapéu, coroa e laço disputam a cabeça.</summary>
public enum Slot { Cabeca, Rosto, Pescoco }

/// <summary>Como o item é desenhado: em cima da cabeça, uma lente em cada olho, ou em volta do pescoço.</summary>
public enum Desenho { Topo, Lentes, Faixa }

/// <param name="Preco">Em moedas. Item exclusivo não se compra (vem de presente).</param>
public sealed record Item(string Id, string Nome, Slot Slot, Desenho Desenho, int Preco, string Descricao, bool Exclusivo = false)
{
    /// <summary>Sprite do item em Assets/sprites/itens (o de óculos é UMA lente).</summary>
    public string Sprite => $"sprites/itens/{Id}.png";
}

/// <summary>
/// Os itens vestíveis. São só visuais: não mudam atributo nenhum, pra ninguém precisar vestir
/// "o certo" para lutar melhor. O tamanho e o encaixe de cada um ficam em
/// <c>Assets/database/ancoras.json</c> (gerado por <c>Arte/gerar_ancoras.py</c>).
/// </summary>
public static class Itens
{
    public static readonly Item Chapeu = new("chapeu", "Chapéu de festa", Slot.Cabeca, Desenho.Topo, 15, "Hoje tem festa!");
    public static readonly Item Laco = new("laco", "Laço rosa", Slot.Cabeca, Desenho.Topo, 15, "Fofo demais.");
    public static readonly Item Coroa = new("coroa", "Coroa dourada", Slot.Cabeca, Desenho.Topo, 0,
        "Prêmio de quem abre o jogo 7 dias seguidos.", Exclusivo: true);
    public static readonly Item Oculos = new("oculos", "Óculos redondos", Slot.Rosto, Desenho.Lentes, 20, "Parece muito inteligente.");
    public static readonly Item OculosSol = new("oculos_sol", "Óculos de sol", Slot.Rosto, Desenho.Lentes, 30, "Cheio de estilo.");
    public static readonly Item Lenco = new("lenco", "Lenço listrado", Slot.Pescoco, Desenho.Faixa, 25, "Para os dias frios.");

    /// <summary>Na ordem em que aparecem no guarda-roupa.</summary>
    public static readonly IReadOnlyList<Item> Todos = [Chapeu, Laco, Coroa, Oculos, OculosSol, Lenco];

    public static Item? Achar(string? id) => id is null ? null : Todos.FirstOrDefault(i => i.Id == id);
}

/// <summary>O que o bichinho está vestindo agora: um item por <see cref="Slot"/>. É do jogador, não
/// do bicho: recomeçar com um ovo novo não perde o guarda-roupa.</summary>
public sealed class Roupa
{
    public string? Cabeca { get; set; }
    public string? Rosto { get; set; }
    public string? Pescoco { get; set; }

    public string? Get(Slot slot) => slot switch
    {
        Slot.Cabeca => Cabeca,
        Slot.Rosto => Rosto,
        _ => Pescoco,
    };

    public void Set(Slot slot, string? id)
    {
        switch (slot)
        {
            case Slot.Cabeca: Cabeca = id; break;
            case Slot.Rosto: Rosto = id; break;
            default: Pescoco = id; break;
        }
    }

    /// <summary>Os itens vestidos agora (ignora id de item que não existe mais).</summary>
    public IEnumerable<Item> Vestidos()
    {
        foreach (var slot in Enum.GetValues<Slot>())
        {
            if (Itens.Achar(Get(slot)) is { } item)
                yield return item;
        }
    }

    public Roupa Copia() => new() { Cabeca = Cabeca, Rosto = Rosto, Pescoco = Pescoco };
}

public enum ResultadoCompra { Ok, JaTem, SemMoedas, NaoSeVende }

/// <summary>Comprar, vestir e tirar. Regra pura sobre o <see cref="Progresso"/>.</summary>
public static class Loja
{
    public static bool Tem(Progresso p, Item item) => p.Guardados.Contains(item.Id);

    public static bool Equipado(Progresso p, Item item) => p.Roupa.Get(item.Slot) == item.Id;

    public static ResultadoCompra Comprar(Progresso p, Item item)
    {
        if (item.Exclusivo)
            return ResultadoCompra.NaoSeVende;
        if (Tem(p, item))
            return ResultadoCompra.JaTem;
        if (p.Moedas < item.Preco)
            return ResultadoCompra.SemMoedas;

        p.Moedas -= item.Preco;
        p.Guardados.Add(item.Id);
        return ResultadoCompra.Ok;
    }

    /// <summary>Entrega sem cobrar (presente do dia). Não faz nada se já tem.</summary>
    public static void Dar(Progresso p, Item item)
    {
        if (!Tem(p, item))
            p.Guardados.Add(item.Id);
    }

    /// <summary>Veste o item, tirando o que estava no mesmo lugar. False se o jogador não tem.</summary>
    public static bool Equipar(Progresso p, Item item)
    {
        if (!Tem(p, item))
            return false;

        p.Roupa.Set(item.Slot, item.Id);
        return true;
    }

    public static void Tirar(Progresso p, Slot slot) => p.Roupa.Set(slot, null);
}
