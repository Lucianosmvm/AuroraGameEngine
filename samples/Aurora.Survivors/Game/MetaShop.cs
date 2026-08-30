using Aurora.Runtime;

namespace Survivors;

/// <summary>Uma melhoria permanente da loja: comprada com moeda, vale em todas as partidas.</summary>
public sealed class MetaItem
{
    /// <summary>Nome da variável do <see cref="GameState"/> que guarda o nível comprado. É uma
    /// variável (e não um campo qualquer) porque o save da engine salva GameState inteiro — o
    /// progresso da loja entra no arquivo sem nenhum código de serialização.</summary>
    public required string Id { get; init; }

    public required string Nome { get; init; }
    public required string Descricao { get; init; }

    public int MaxNivel { get; init; } = 5;

    /// <summary>Preço do primeiro nível.</summary>
    public int PrecoBase { get; init; } = 25;

    /// <summary>Quanto o preço sobe a cada nível comprado.</summary>
    public int PrecoPorNivel { get; init; } = 20;
}

/// <summary>
/// Loja entre partidas: gasta a moeda acumulada em bônus permanentes. Os níveis comprados viram
/// bônus na ficha do jogador no início de cada partida (<see cref="AplicarEm"/>, chamado pelo
/// <see cref="PlayerRunner"/>).
///
/// <para>Pra vender coisa nova, acrescente um <see cref="MetaItem"/> em <see cref="Itens"/> e
/// trate o Id dele em <see cref="AplicarEm"/>. A tela mostra até 4 itens — mexer em
/// Assets/scenes/Loja.json acrescenta linhas.</para>
/// </summary>
public static class MetaShop
{
    /// <summary>Item do inventário usado como dinheiro. É salvo junto com o resto do inventário.</summary>
    public const string Moeda = "Moeda";

    public static readonly IReadOnlyList<MetaItem> Itens =
    [
        new()
        {
            Id = "MetaVida", Nome = "Vitalidade", Descricao = "+20 de vida máxima por nível",
            PrecoBase = 20, PrecoPorNivel = 15, MaxNivel = 5,
        },
        new()
        {
            Id = "MetaDano", Nome = "Fúria", Descricao = "+10% de dano por nível",
            PrecoBase = 30, PrecoPorNivel = 25, MaxNivel = 5,
        },
        new()
        {
            Id = "MetaVelocidade", Nome = "Pés Ligeiros", Descricao = "+6% de velocidade por nível",
            PrecoBase = 25, PrecoPorNivel = 20, MaxNivel = 4,
        },
        new()
        {
            Id = "MetaColeta", Nome = "Ímã Antigo", Descricao = "+20 de raio de coleta por nível",
            PrecoBase = 20, PrecoPorNivel = 15, MaxNivel = 4,
        },
    ];

    public static int Nivel(GameState state, MetaItem item) => (int)state.GetVariable(item.Id);

    public static bool NoMaximo(GameState state, MetaItem item) => Nivel(state, item) >= item.MaxNivel;

    public static int Preco(GameState state, MetaItem item)
        => item.PrecoBase + item.PrecoPorNivel * Nivel(state, item);

    /// <summary>Tenta comprar um nível. False quando está no teto ou falta moeda — a mensagem
    /// explica qual dos dois, pra tela não precisar repetir a regra.</summary>
    public static bool Comprar(GameState state, InventoryManager inventario, MetaItem item, out string mensagem)
    {
        if (NoMaximo(state, item))
        {
            mensagem = $"{item.Nome} já está no máximo.";
            return false;
        }

        int preco = Preco(state, item);
        if (inventario.GetCount(Moeda) < preco)
        {
            mensagem = $"Faltam moedas para {item.Nome} ({preco}).";
            return false;
        }

        inventario.Remove(Moeda, preco);
        state.AddVariable(item.Id, 1f);
        mensagem = $"{item.Nome} nível {Nivel(state, item)} comprado!";
        return true;
    }

    /// <summary>Soma os bônus comprados na ficha do jogador. Chamado uma vez, no início da
    /// partida — depois disso a ficha é mexida só pelos upgrades de level up.</summary>
    public static void AplicarEm(PlayerStats stats, GameState state)
    {
        stats.MaxHealth += 20f * state.GetVariable("MetaVida");
        stats.DamageMultiplier += 0.10f * state.GetVariable("MetaDano");
        stats.MoveSpeed *= 1f + 0.06f * state.GetVariable("MetaVelocidade");
        stats.PickupRadius += 20f * state.GetVariable("MetaColeta");
    }
}
