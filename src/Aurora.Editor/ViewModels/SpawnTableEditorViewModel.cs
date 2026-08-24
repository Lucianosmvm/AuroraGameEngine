using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

/// <summary>Uma possibilidade dentro de uma tabela: qual prefab, com que peso, sob que condição.</summary>
public sealed class SpawnEntryViewModel : ViewModelBase
{
    private readonly JsonObject _node;
    private readonly Action _onEdited;
    private readonly MainViewModel? _owner;

    public JsonObject Node => _node;
    public ICommand RemoveCommand { get; }

    public SpawnEntryViewModel(JsonObject node, Action onEdited, Action<SpawnEntryViewModel> onRemove,
        MainViewModel? owner = null)
    {
        _node = node;
        _onEdited = onEdited;
        _owner = owner;
        RemoveCommand = new RelayCommand(() => onRemove(this));
        RebuildCondition();
    }

    /// <summary>Prefabs do projeto, pro seletor — digitar caminho na mão é o jeito mais fácil de
    /// criar uma entrada que nunca nasce e não avisa.</summary>
    public IEnumerable<string> PrefabOptions => _owner?.Prefabs.Select(p => p.RelativePath) ?? [];

    public string Prefab
    {
        get => _node["Prefab"]?.GetValue<string>() ?? "";
        set { _node["Prefab"] = value; Raise(); _onEdited(); }
    }

    public string WeightText
    {
        get => _node["Weight"].AsFloat(1f)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (!float.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                return;

            _node["Weight"] = parsed;
            Raise();
            _onEdited();
        }
    }

    /// <summary>
    /// Se esta entrada tem condição. Ligar cria um nó de ação If; desligar remove — sem meio
    /// termo, porque uma condição vazia no arquivo seria lida como "nunca elegível" e a entrada
    /// sumiria do sorteio sem explicação.
    /// </summary>
    public bool HasCondition
    {
        get => _node["Condition"] is JsonObject;
        set
        {
            if (value == HasCondition)
                return;

            if (value)
                _node["Condition"] = new JsonObject
                {
                    ["Action"] = "If",
                    ["Text"] = "Switch",
                    ["On"] = true,
                };
            else
                _node.Remove("Condition");

            RebuildCondition();
            Raise();
            Raise(nameof(Condition));
            _onEdited();
        }
    }

    /// <summary>A condição é uma ação If de verdade — o mesmo editor, o mesmo formato, a mesma
    /// avaliação em runtime. Null quando não há condição.</summary>
    public EventActionViewModel? Condition { get; private set; }

    private void RebuildCondition()
        => Condition = _node["Condition"] is JsonObject condition
            ? new EventActionViewModel(condition, _onEdited, _ => { }, _owner)
            : null;
}

/// <summary>Uma tabela nomeada: o id que a cena escreve no lugar de um caminho de prefab.</summary>
public sealed class SpawnTableRowViewModel : ViewModelBase
{
    private readonly JsonObject _node;
    private readonly Action _onEdited;

    public JsonObject Node => _node;

    public SpawnTableRowViewModel(JsonObject node, Action onEdited)
    {
        _node = node;
        _onEdited = onEdited;
    }

    public string Id
    {
        get => _node["Id"]?.GetValue<string>() ?? "";
        set { _node["Id"] = value; Raise(); Raise(nameof(Display)); _onEdited(); }
    }

    public string Display => Id.Length > 0 ? Id : "(sem id)";

    public string Summary
    {
        get
        {
            int count = (_node["Entries"] as JsonArray)?.Count ?? 0;
            return count == 1 ? "1 entrada" : $"{count} entradas";
        }
    }

    internal void RaiseSummary() => Raise(nameof(Summary));
}

/// <summary>
/// Aba "Tabelas de Spawn" do banco de dados. Edita <c>Assets/database/spawns.json</c>: grupos de
/// prefabs sorteados por peso, cada um com condição opcional.
///
/// <para>É o que permite a cena dizer "nasce um inimigo da floresta" em vez de nomear um arquivo
/// — trocar o que existe no grupo (ou acrescentar o zumbi que só sai de noite) deixa de mexer
/// em cena nenhuma.</para>
/// </summary>
public sealed class SpawnTableEditorViewModel : ViewModelBase
{
    private readonly string _path;
    private readonly MainViewModel? _owner;
    private JsonObject _root = new();

    public ObservableCollection<SpawnTableRowViewModel> Tables { get; } = [];
    public ObservableCollection<SpawnEntryViewModel> Entries { get; } = [];

    public ICommand AddTableCommand { get; }
    public ICommand RemoveTableCommand { get; }
    public ICommand AddEntryCommand { get; }

    private SpawnTableRowViewModel? _selected;
    public SpawnTableRowViewModel? Selected
    {
        get => _selected;
        set { _selected = value; Raise(); Raise(nameof(HasSelection)); RebuildEntries(); }
    }

    public bool HasSelection => _selected is not null;

    private string _status = "";
    public string Status
    {
        get => _status;
        private set { _status = value; Raise(); }
    }

    public SpawnTableEditorViewModel(string spawnsJsonPath, MainViewModel? owner = null)
    {
        _path = spawnsJsonPath;
        _owner = owner;
        AddTableCommand = new RelayCommand(AddTable);
        RemoveTableCommand = new RelayCommand(RemoveSelected);
        AddEntryCommand = new RelayCommand(AddEntry);
        Load();
    }

    private void Load()
    {
        Tables.Clear();

        if (_path.Length == 0 || !File.Exists(_path))
        {
            _root = new JsonObject { ["Tables"] = new JsonArray() };
            Status = "Nenhuma tabela ainda.";
            return;
        }

        try
        {
            _root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                    ?? new JsonObject { ["Tables"] = new JsonArray() };
        }
        catch (Exception ex)
        {
            // Não sobrescreve o arquivo quebrado — ver ItemDatabaseViewModel, mesma razão.
            _root = new JsonObject { ["Tables"] = new JsonArray() };
            Status = $"spawns.json inválido ({ex.Message}) — não salve por cima sem conferir.";
            return;
        }

        if (_root["Tables"] is not JsonArray array)
            _root["Tables"] = array = new JsonArray();

        foreach (var node in array.OfType<JsonObject>())
            Tables.Add(new SpawnTableRowViewModel(node, MarkDirty));

        Status = $"{Tables.Count} tabela(s).";
    }

    private void MarkDirty() => Status = "Alterado — clique em Salvar.";

    private void AddTable()
    {
        var node = new JsonObject { ["Id"] = UniqueId(), ["Entries"] = new JsonArray() };
        ((JsonArray)_root["Tables"]!).Add(node);

        var row = new SpawnTableRowViewModel(node, MarkDirty);
        Tables.Add(row);
        Selected = row;
        MarkDirty();
    }

    private string UniqueId()
    {
        const string baseId = "grupo_novo";
        if (Tables.All(t => t.Id != baseId))
            return baseId;

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseId}_{n}";
            if (Tables.All(t => t.Id != candidate))
                return candidate;
        }
    }

    private void RemoveSelected()
    {
        if (Selected is not { } row)
            return;

        ((JsonArray)_root["Tables"]!).Remove(row.Node);
        Tables.Remove(row);
        Selected = Tables.FirstOrDefault();
        MarkDirty();
    }

    private void RebuildEntries()
    {
        Entries.Clear();

        if (Selected is not { } row)
            return;

        if (row.Node["Entries"] is not JsonArray entries)
            row.Node["Entries"] = entries = new JsonArray();

        foreach (var entry in entries.OfType<JsonObject>())
            Entries.Add(BuildEntry(entry, entries));
    }

    private SpawnEntryViewModel BuildEntry(JsonObject entry, JsonArray owner)
        => new(entry,
            onEdited: MarkDirty,
            onRemove: vm =>
            {
                owner.Remove(entry);
                Entries.Remove(vm);
                MarkDirty();
                Selected?.RaiseSummary();
            },
            owner: _owner);

    private void AddEntry()
    {
        if (Selected is not { } row || row.Node["Entries"] is not JsonArray entries)
            return;

        var entry = new JsonObject { ["Prefab"] = "", ["Weight"] = 1 };
        entries.Add(entry);
        Entries.Add(BuildEntry(entry, entries));
        MarkDirty();
        row.RaiseSummary();
    }

    public void Save()
    {
        if (_path.Length == 0)
        {
            Status = "Sem projeto aberto — nada a salvar.";
            return;
        }

        var duplicates = Tables.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
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
        Status = $"Salvo: {Path.GetFileName(_path)} ({Tables.Count} tabela(s)).";
    }
}
