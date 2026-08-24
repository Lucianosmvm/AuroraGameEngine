using Aurora.Runtime.Database;

namespace Aurora.Runtime.UI;

/// <summary>
/// Loja: compra e venda de itens usando a caixa de diálogo que já existe.
///
/// <para>Não desenha nada de novo — é uma <see cref="DialogueChoice"/> em laço, uma opção por
/// mercadoria, reabrindo depois de cada compra até o jogador escolher "Sair". Assim a loja
/// funciona com o mesmo input, a mesma fonte e a mesma caixa do resto do jogo, e um jogo que não
/// tem loja não carrega nem uma linha disto.</para>
///
/// <para>Moeda não é conceito da engine: é uma variável do <see cref="GameState"/> escolhida por
/// quem monta a loja (<c>Ouro</c>, <c>Créditos</c>, <c>Munição</c>…). Quem não quiser economia
/// nenhuma simplesmente não usa a ação.</para>
/// </summary>
public sealed class ShopSystem
{
    private readonly DialogueSystem _dialogue;
    private readonly InventoryManager _inventory;
    private readonly ItemDatabase _items;
    private readonly GameState _state;
    private readonly TermDatabase? _terms;

    /// <summary>Fração do preço paga ao jogador na venda quando a ação não manda outra. Meio
    /// preço é a convenção do gênero — comprar e revender no mesmo balcão não pode dar lucro.</summary>
    public const float DefaultSellRate = 0.5f;

    /// <param name="terms">Banco de termos, pra trocar as palavras da loja sem mexer em código
    /// ("Comprar" virar "Trocar"). Null = os padrões em português.</param>
    public ShopSystem(DialogueSystem dialogue, InventoryManager inventory, ItemDatabase items,
        GameState state, TermDatabase? terms = null)
    {
        _dialogue = dialogue;
        _inventory = inventory;
        _items = items;
        _state = state;
        _terms = terms;
    }

    /// <summary>Texto de interface da loja, com o padrão embutido quando não há cadastro.</summary>
    private string Term(string key, string fallback) => _terms?.Get(key, fallback) ?? fallback;

    /// <summary>
    /// Abre a loja.
    /// </summary>
    /// <param name="goods">Ids à venda. Id fora do banco é avisado e ignorado.</param>
    /// <param name="currency">Variável do GameState que guarda o dinheiro.</param>
    /// <param name="mode">"Buy" (padrão), "Sell" ou "Both".</param>
    /// <param name="sellRate">Fração do preço na venda. 0 = <see cref="DefaultSellRate"/>.</param>
    public void Open(IReadOnlyList<string> goods, string currency, string mode, float sellRate)
    {
        currency = currency.Length > 0 ? currency : "Ouro";
        sellRate = sellRate > 0f ? sellRate : DefaultSellRate;

        bool canBuy = !mode.Equals("Sell", StringComparison.OrdinalIgnoreCase);
        bool canSell = mode.Equals("Sell", StringComparison.OrdinalIgnoreCase)
                       || mode.Equals("Both", StringComparison.OrdinalIgnoreCase);

        if (canBuy && canSell)
            ShowCounter(goods, currency, sellRate);
        else if (canSell)
            ShowSellList(goods, currency, sellRate);
        else
            ShowBuyList(goods, currency, sellRate);
    }

    /// <summary>Balcão do modo "Both": escolher entre comprar e vender antes de ver a lista.</summary>
    private void ShowCounter(IReadOnlyList<string> goods, string currency, float sellRate)
    {
        string[] options = [Term("shop.buy", "Comprar"), Term("shop.sell", "Vender"), Term("shop.exit", "Sair")];
        _dialogue.ShowChoice(Wallet(currency), options, index =>
        {
            if (index == 0) ShowBuyList(goods, currency, sellRate, backToCounter: true);
            else if (index == 1) ShowSellList(goods, currency, sellRate, backToCounter: true);
        });
    }

    private void ShowBuyList(IReadOnlyList<string> goods, string currency, float sellRate, bool backToCounter = false)
    {
        var offers = new List<ItemDefinition>();
        foreach (string id in goods)
        {
            if (_items.Get(id) is { } definition)
                offers.Add(definition);
            else
                Console.Error.WriteLine($"[ShopSystem] Item '{id}' não está no banco — fora da loja.");
        }

        if (offers.Count == 0)
        {
            _dialogue.ShowMessage(Term("shop.empty", "Não tenho nada pra vender hoje."));
            return;
        }

        var options = offers.Select(o => $"{Label(o)} — {o.Price}").ToList();
        options.Add(Term("shop.exit", "Sair"));

        _dialogue.ShowChoice(Wallet(currency), options, index =>
        {
            if (index >= offers.Count)
            {
                if (backToCounter) ShowCounter(goods, currency, sellRate);
                return;
            }

            Buy(offers[index], currency);
            ShowBuyList(goods, currency, sellRate, backToCounter);
        });
    }

    private void ShowSellList(IReadOnlyList<string> goods, string currency, float sellRate, bool backToCounter = false)
    {
        // Vende o que está na mochila e tem preço — não a lista da loja: obrigar o jogador a
        // procurar o comerciante certo pra cada tralha seria só atrito.
        var sellable = _inventory.Items.Keys
            .Select(id => _items.Get(id))
            .OfType<ItemDefinition>()
            .Where(definition => definition.Price > 0)
            .OrderBy(definition => Label(definition))
            .ToList();

        if (sellable.Count == 0)
        {
            _dialogue.ShowMessage(Term("shop.nothingToSell", "Você não tem nada que eu queira comprar."));
            if (backToCounter)
                ShowCounter(goods, currency, sellRate);
            return;
        }

        var options = sellable
            .Select(definition => $"{Label(definition)} x{_inventory.GetCount(definition.Id)} — {SellPrice(definition, sellRate)}")
            .ToList();
        options.Add(Term("shop.exit", "Sair"));

        _dialogue.ShowChoice(Wallet(currency), options, index =>
        {
            if (index >= sellable.Count)
            {
                if (backToCounter) ShowCounter(goods, currency, sellRate);
                return;
            }

            Sell(sellable[index], currency, sellRate);
            ShowSellList(goods, currency, sellRate, backToCounter);
        });
    }

    private void Buy(ItemDefinition definition, string currency)
    {
        float money = _state.GetVariable(currency);
        if (money < definition.Price)
        {
            _dialogue.ShowMessage(Term("shop.cantAfford", "Não dá pro seu bolso."));
            return;
        }

        // Cobra pelo que ENTROU: com MaxStack cheio o inventário aceita 0, e cobrar mesmo assim
        // seria roubar o jogador por causa de um limite que ele não vê.
        int added = _inventory.Add(definition.Id, 1);
        if (added <= 0)
        {
            _dialogue.ShowMessage(Term("shop.full", "Você já carrega o quanto pode disso."));
            return;
        }

        _state.SetVariable(currency, money - definition.Price * added);
    }

    private void Sell(ItemDefinition definition, string currency, float sellRate)
    {
        if (_inventory.Remove(definition.Id, 1) == 0)
            return;

        _state.AddVariable(currency, SellPrice(definition, sellRate));
    }

    private static int SellPrice(ItemDefinition definition, float sellRate)
        => Math.Max(0, (int)MathF.Floor(definition.Price * sellRate));

    private static string Label(ItemDefinition definition)
        => definition.Name.Length > 0 ? definition.Name : definition.Id;

    private string Wallet(string currency)
        => $"{currency}: {_state.GetVariable(currency):0}";
}
