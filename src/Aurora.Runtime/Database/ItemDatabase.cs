using System.Text.Json;
using Aurora.Runtime.Events;

namespace Aurora.Runtime.Database;

/// <summary>
/// Ficha de um item: o que o <see cref="InventoryManager"/> não guarda. O inventário é só
/// "id → quantidade"; é aqui que mora o nome que aparece na tela, o ícone, o preço da loja e o
/// que acontece ao usar.
/// </summary>
public sealed class ItemDefinition
{
    /// <summary>Chave usada no inventário, nas ações AddItem/RemoveItem/UseItem e no gatilho
    /// HasItem. É o "ID" do item.</summary>
    public string Id = "";

    /// <summary>Nome exibido pro jogador. Vazio = usa o Id.</summary>
    public string Name = "";

    /// <summary>Textura do ícone, relativa a Assets. Vazio = sem ícone.</summary>
    public string Icon = "";

    public string Description = "";

    /// <summary>Categoria livre ("Consumivel", "Arma", "Material", "Chave"…). A engine não impõe
    /// significado: serve pra HUD separar abas e pra loja filtrar. Um jogo de fazenda quer
    /// "Semente", um de terror quer "Documento" — decidir por você limitaria os dois.</summary>
    public string Type = "";

    /// <summary>Máximo por pilha na interface. 0 = sem limite.</summary>
    public int MaxStack;

    /// <summary>Preço base pra loja. 0 = não vendável.</summary>
    public int Price;

    /// <summary>Se usar consome uma unidade. Poção sim, chave não.</summary>
    public bool Consumable = true;

    /// <summary>
    /// O que acontece ao usar, na MESMA lista de ações dos eventos visuais — "poção cura 50" é
    /// <c>[{ "Action": "Heal", "Value": 50 }]</c>.
    ///
    /// <para>Reaproveitar EventAction em vez de inventar um formato de efeito é o que faz item
    /// novo não precisar de código: tudo que um evento sabe fazer (curar, dar dano, teleportar,
    /// tocar som, ligar switch, instanciar prefab, mexer em quest) um item também sabe, e o
    /// editor de ações que já existe serve pros dois.</para>
    /// </summary>
    public List<EventAction> Effect = [];
}

/// <summary>
/// Catálogo de itens do jogo, carregado de um JSON em Assets (por convenção
/// <c>database/items.json</c>). É o "banco de dados" no sentido do RPG Maker: os itens existem
/// como dado editável, não como código, e são referenciados por id.
///
/// <para>Inimigos e objetos de cena NÃO ficam aqui — aquilo é prefab, e o caminho do arquivo já
/// é o identificador. Duplicar isso num banco separado só daria dois lugares pra desencontrar.</para>
/// </summary>
public sealed class ItemDatabase
{
    private readonly Dictionary<string, ItemDefinition> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Caminho padrão procurado pelo Game no boot.</summary>
    public const string DefaultPath = "database/items.json";

    public IReadOnlyDictionary<string, ItemDefinition> Items => _items;

    public int Count => _items.Count;

    /// <summary>Ficha do item, ou null se o id não existe no banco. Id desconhecido não é erro:
    /// um jogo pode usar o inventário sem banco nenhum, com itens só de contagem.</summary>
    public ItemDefinition? Get(string id)
        => _items.TryGetValue(id, out var item) ? item : null;

    /// <summary>Nome de exibição, caindo no próprio id quando não há ficha — assim uma HUD nunca
    /// mostra vazio por causa de um item que ninguém cadastrou.</summary>
    public string DisplayName(string id) => Get(id)?.Name is { Length: > 0 } name ? name : id;

    public void Add(ItemDefinition item) => _items[item.Id] = item;

    public void Clear() => _items.Clear();

    /// <summary>
    /// Lê o catálogo de um JSON <c>{ "Items": [ … ] }</c>. Substitui o conteúdo atual.
    /// Item sem Id é ignorado com aviso: sem chave ele seria inalcançável de qualquer jeito, e
    /// derrubar o boot do jogo por causa de uma linha incompleta seria pior.
    /// </summary>
    public void Load(string json)
    {
        _items.Clear();

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Items", out var items))
            return;

        foreach (var element in items.EnumerateArray())
        {
            string id = GetString(element, "Id");
            if (id.Length == 0)
            {
                Console.Error.WriteLine("[ItemDatabase] Item sem \"Id\" no banco — ignorado.");
                continue;
            }

            var item = new ItemDefinition
            {
                Id = id,
                Name = GetString(element, "Name"),
                Icon = GetString(element, "Icon"),
                Description = GetString(element, "Description"),
                Type = GetString(element, "Type"),
                MaxStack = element.TryGetProperty("MaxStack", out var stack) ? stack.GetInt32() : 0,
                Price = element.TryGetProperty("Price", out var price) ? price.GetInt32() : 0,
                Consumable = !element.TryGetProperty("Consumable", out var consumable) || consumable.GetBoolean(),
            };

            if (element.TryGetProperty("Effect", out var effect))
                item.Effect = EventAction.ParseList(effect);

            _items[id] = item;
        }
    }

    private static string GetString(JsonElement json, string name)
        => json.TryGetProperty(name, out var prop) ? prop.GetString() ?? "" : "";
}
