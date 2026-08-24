using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

/// <summary>Uma linha do banco: a ficha de um item.</summary>
public sealed class ItemRowViewModel : ViewModelBase
{
    private readonly JsonObject _node;
    private readonly Action _onEdited;

    /// <summary>Nó cru do item — a lista de efeitos é editada por cima dele.</summary>
    public JsonObject Node => _node;

    public ItemRowViewModel(JsonObject node, Action onEdited)
    {
        _node = node;
        _onEdited = onEdited;
    }

    /// <summary>Rótulo da lista: o nome quando existe, senão o id — item recém-criado ainda não
    /// tem nome e sumiria da lista se o rótulo dependesse só dele.</summary>
    public string Display => Name.Length > 0 ? $"{Name}  ({Id})" : Id;

    private string Str(string key) => _node[key]?.GetValue<string>() ?? "";

    private void SetStr(string key, string value, bool removeWhenEmpty = true)
    {
        if (removeWhenEmpty && string.IsNullOrEmpty(value)) _node.Remove(key);
        else _node[key] = value;
        Raise(nameof(Display));
        _onEdited();
    }

    public string Id
    {
        get => Str("Id");
        set { SetStr("Id", value, removeWhenEmpty: false); Raise(); }
    }

    public string Name
    {
        get => Str("Name");
        set { SetStr("Name", value); Raise(); }
    }

    public string Icon
    {
        get => Str("Icon");
        set { SetStr("Icon", value); Raise(); }
    }

    public string Description
    {
        get => Str("Description");
        set { SetStr("Description", value); Raise(); }
    }

    public string Type
    {
        get => Str("Type");
        set { SetStr("Type", value); Raise(); }
    }

    public string MaxStackText
    {
        get => _node["MaxStack"].AsInt(0).ToString();
        set
        {
            if (!int.TryParse(value, out int parsed)) return;
            if (parsed <= 0) _node.Remove("MaxStack"); else _node["MaxStack"] = parsed;
            Raise();
            _onEdited();
        }
    }

    public string PriceText
    {
        get => _node["Price"].AsInt(0).ToString();
        set
        {
            if (!int.TryParse(value, out int parsed)) return;
            if (parsed <= 0) _node.Remove("Price"); else _node["Price"] = parsed;
            Raise();
            _onEdited();
        }
    }

    public bool Consumable
    {
        get => _node["Consumable"]?.GetValue<bool>() ?? true;
        set
        {
            if (value) _node.Remove("Consumable"); else _node["Consumable"] = false;
            Raise();
            _onEdited();
        }
    }

    /// <summary>Resumo do efeito pra lista não obrigar a abrir cada item pra saber o que ele faz.</summary>
    public string EffectSummary
    {
        get
        {
            if (_node["Effect"] is not JsonArray effect || effect.Count == 0)
                return "sem efeito";

            var names = effect.OfType<JsonObject>()
                .Select(a => a["Action"]?.GetValue<string>() ?? "?")
                .ToList();

            return string.Join(" → ", names);
        }
    }

    internal void RaiseEffectSummary() => Raise(nameof(EffectSummary));
}

/// <summary>
/// Banco de dados de itens do projeto — o equivalente à aba "Itens" do RPG Maker. Edita
/// <c>Assets/database/items.json</c>, que é o arquivo que o runtime carrega sozinho no boot.
///
/// <para>Só itens. Inimigo e objeto de cena são PREFAB (o caminho do arquivo já é o id), e
/// duplicá-los aqui só criaria dois lugares pra desencontrar.</para>
/// </summary>
public sealed class ItemDatabaseViewModel : ViewModelBase
{
    private readonly string _path;
    private JsonObject _root = new();

    public ObservableCollection<ItemRowViewModel> Items { get; } = [];

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand AddEffectCommand { get; }

    private ItemRowViewModel? _selected;
    public ItemRowViewModel? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Raise();
            Raise(nameof(HasSelection));
            RebuildEffects();
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>Ações do efeito do item selecionado, reusando o mesmo editor de ações dos
    /// eventos — item novo não precisa de nenhum conceito novo pra fazer alguma coisa.</summary>
    public ObservableCollection<EventActionViewModel> Effects { get; } = [];

    private string _status = "";
    public string Status
    {
        get => _status;
        private set { _status = value; Raise(); }
    }

    public ItemDatabaseViewModel(string itemsJsonPath)
    {
        _path = itemsJsonPath;
        AddCommand = new RelayCommand(AddItem);
        RemoveCommand = new RelayCommand(RemoveSelected);
        AddEffectCommand = new RelayCommand(AddEffect);
        Load();
    }

    private void Load()
    {
        Items.Clear();

        if (_path.Length == 0 || !File.Exists(_path))
        {
            _root = new JsonObject { ["Items"] = new JsonArray() };
            Status = "Banco novo — nada salvo ainda.";
            return;
        }

        try
        {
            _root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                    ?? new JsonObject { ["Items"] = new JsonArray() };
        }
        catch (Exception ex)
        {
            // Não sobrescreve o arquivo quebrado: quem abriu o banco pode não querer perder o
            // que estava lá, e salvar por cima seria irreversível.
            _root = new JsonObject { ["Items"] = new JsonArray() };
            Status = $"items.json inválido ({ex.Message}) — não salve por cima sem conferir.";
            return;
        }

        if (_root["Items"] is not JsonArray array)
            _root["Items"] = array = new JsonArray();

        foreach (var node in array.OfType<JsonObject>())
            Items.Add(new ItemRowViewModel(node, MarkDirty));

        Status = $"{Items.Count} item(ns).";
    }

    private void MarkDirty() => Status = "Alterado — clique em Salvar.";

    private void AddItem()
    {
        var node = new JsonObject { ["Id"] = UniqueId(), ["Effect"] = new JsonArray() };
        ((JsonArray)_root["Items"]!).Add(node);

        var row = new ItemRowViewModel(node, MarkDirty);
        Items.Add(row);
        Selected = row;
        MarkDirty();
    }

    /// <summary>"item_novo", "item_novo_2"… — id repetido faz o runtime perder um dos dois
    /// silenciosamente, porque o banco é indexado por id.</summary>
    private string UniqueId()
    {
        const string baseId = "item_novo";
        if (Items.All(i => i.Id != baseId))
            return baseId;

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseId}_{n}";
            if (Items.All(i => i.Id != candidate))
                return candidate;
        }
    }

    private void RemoveSelected()
    {
        if (Selected is not { } row)
            return;

        ((JsonArray)_root["Items"]!).Remove(row.Node);
        Items.Remove(row);
        Selected = Items.FirstOrDefault();
        MarkDirty();
    }

    private void RebuildEffects()
    {
        Effects.Clear();

        if (Selected is not { } row)
            return;

        if (row.Node["Effect"] is not JsonArray effect)
            row.Node["Effect"] = effect = new JsonArray();

        foreach (var action in effect.OfType<JsonObject>())
            Effects.Add(BuildEffectViewModel(action, effect));
    }

    private EventActionViewModel BuildEffectViewModel(JsonObject action, JsonArray owner)
        => new(action,
            onEdited: () => { MarkDirty(); Selected?.RaiseEffectSummary(); },
            onRemove: vm =>
            {
                owner.Remove(action);
                Effects.Remove(vm);
                MarkDirty();
                Selected?.RaiseEffectSummary();
            });

    private void AddEffect()
    {
        if (Selected is not { } row || row.Node["Effect"] is not JsonArray effect)
            return;

        var action = new JsonObject { ["Action"] = "Heal", ["Value"] = 50f };
        effect.Add(action);
        Effects.Add(BuildEffectViewModel(action, effect));
        MarkDirty();
        row.RaiseEffectSummary();
    }

    /// <summary>Grava o banco. Cria a pasta database/ se ainda não existir.</summary>
    public void Save()
    {
        if (_path.Length == 0)
        {
            Status = "Sem projeto aberto — nada a salvar.";
            return;
        }

        var duplicates = Items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            Status = $"Id repetido: {string.Join(", ", duplicates)} — o runtime só enxergaria um deles.";
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Status = $"Salvo: {Path.GetFileName(_path)} ({Items.Count} item(ns)).";
    }
}
