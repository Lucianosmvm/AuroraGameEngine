using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

/// <summary>Um evento comum na lista: id, rótulo e quando dispara sozinho.</summary>
public sealed class CommonEventRowViewModel : ViewModelBase
{
    private readonly Action _onEdited;

    public JsonObject Node { get; }

    public CommonEventRowViewModel(JsonObject node, Action onEdited)
    {
        Node = node;
        _onEdited = onEdited;
    }

    /// <summary>Como o evento dispara sozinho. Os valores são os que o runtime lê — ver
    /// CommonEventDefinition.Trigger.</summary>
    public string[] Triggers { get; } = ["Manual", "OnSwitchOn", "WhileSwitchOn"];

    public string Id
    {
        get => Node["Id"]?.GetValue<string>() ?? "";
        set { Node["Id"] = value; Raise(); Raise(nameof(Display)); _onEdited(); }
    }

    public string Name
    {
        get => Node["Name"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) Node.Remove("Name");
            else Node["Name"] = value;
            Raise();
            Raise(nameof(Display));
            _onEdited();
        }
    }

    public string Trigger
    {
        get => Node["Trigger"]?.GetValue<string>() ?? "Manual";
        set
        {
            if (value == "Manual") Node.Remove("Trigger");
            else Node["Trigger"] = value;
            Raise();
            Raise(nameof(ShowSwitch));
            Raise(nameof(TriggerHint));
            Raise(nameof(Summary));
            _onEdited();
        }
    }

    /// <summary>Switch que liga o disparo automático. Só aparece com Trigger automático — sem ele
    /// o evento nunca dispara, e é isso que impede um cadastro novo de começar a rodar sozinho.</summary>
    public string Switch
    {
        get => Node["Switch"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) Node.Remove("Switch");
            else Node["Switch"] = value;
            Raise();
            Raise(nameof(Summary));
            _onEdited();
        }
    }

    public bool ShowSwitch => Trigger != "Manual";

    public string TriggerHint => Trigger switch
    {
        "OnSwitchOn" => "Dispara uma vez, no instante em que o switch liga.",
        "WhileSwitchOn" => "Dispara TODO FRAME enquanto o switch estiver ligado. Use com parcimônia.",
        _ => "Só roda quando alguém chama com a ação CallEvent.",
    };

    public string Display => Name.Length > 0 ? $"{Name}  ({Id})" : Id;

    public string Summary
    {
        get
        {
            int count = (Node["Actions"] as JsonArray)?.Count ?? 0;
            string actions = count == 1 ? "1 ação" : $"{count} ações";
            return Trigger == "Manual" ? actions : $"{actions} · {Trigger} [{Switch}]";
        }
    }

    internal void RaiseSummary() => Raise(nameof(Summary));
}

/// <summary>
/// Aba "Eventos Comuns" do banco. Edita <c>Assets/database/common_events.json</c>: sequências de
/// ações cadastradas por id e chamadas de qualquer cena, item ou botão de UI pela ação CallEvent.
///
/// <para>É o que tira a cópia-e-cola de sequência entre cenas: "abrir baú" existe uma vez, e os
/// quarenta baús do jogo chamam o mesmo id. Reusa o editor de ações dos eventos — nenhum conceito
/// novo pra quem já monta gatilho.</para>
/// </summary>
public sealed class CommonEventEditorViewModel : ViewModelBase
{
    private readonly string _path;
    private readonly MainViewModel? _owner;
    private JsonObject _root = new();

    public ObservableCollection<CommonEventRowViewModel> Events { get; } = [];
    public ObservableCollection<EventActionViewModel> Actions { get; } = [];

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand AddActionCommand { get; }
    public ICommand DuplicateCommand { get; }

    private CommonEventRowViewModel? _selected;
    public CommonEventRowViewModel? Selected
    {
        get => _selected;
        set { _selected = value; Raise(); Raise(nameof(HasSelection)); RebuildActions(); }
    }

    public bool HasSelection => _selected is not null;

    private string _status = "";
    public string Status
    {
        get => _status;
        private set { _status = value; Raise(); }
    }

    public CommonEventEditorViewModel(string jsonPath, MainViewModel? owner = null)
    {
        _path = jsonPath;
        _owner = owner;
        AddCommand = new RelayCommand(AddEvent);
        RemoveCommand = new RelayCommand(RemoveSelected);
        AddActionCommand = new RelayCommand(AddAction);
        DuplicateCommand = new RelayCommand(DuplicateSelected);
        Load();
    }

    private void Load()
    {
        Events.Clear();

        if (_path.Length == 0 || !File.Exists(_path))
        {
            _root = new JsonObject { ["Events"] = new JsonArray() };
            Status = "Nenhum evento comum ainda.";
            return;
        }

        try
        {
            _root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                    ?? new JsonObject { ["Events"] = new JsonArray() };
        }
        catch (Exception ex)
        {
            // Não sobrescreve arquivo quebrado: salvar por cima apagaria o que o autor escreveu
            // à mão e ainda não conseguiu ler de volta.
            _root = new JsonObject { ["Events"] = new JsonArray() };
            Status = $"common_events.json inválido ({ex.Message}) — não salve por cima sem conferir.";
            return;
        }

        if (_root["Events"] is not JsonArray array)
            _root["Events"] = array = new JsonArray();

        foreach (var node in array.OfType<JsonObject>())
            Events.Add(new CommonEventRowViewModel(node, MarkDirty));

        Status = $"{Events.Count} evento(s).";
    }

    private void MarkDirty() => Status = "Alterado — clique em Salvar.";

    private void AddEvent()
    {
        var node = new JsonObject { ["Id"] = UniqueId("evento_novo"), ["Actions"] = new JsonArray() };
        ((JsonArray)_root["Events"]!).Add(node);

        var row = new CommonEventRowViewModel(node, MarkDirty);
        Events.Add(row);
        Selected = row;
        MarkDirty();
    }

    /// <summary>Copia o evento selecionado inteiro (ações inclusive). Cadastrar a variação de uma
    /// sequência de dez ações do zero é o tipo de trabalho que faz o autor desistir de usar o
    /// banco e voltar a copiar ação por ação na cena.</summary>
    private void DuplicateSelected()
    {
        if (Selected is not { } row || row.Node.DeepClone() is not JsonObject copy)
            return;

        copy["Id"] = UniqueId($"{row.Id}_copia");
        ((JsonArray)_root["Events"]!).Add(copy);

        var added = new CommonEventRowViewModel(copy, MarkDirty);
        Events.Add(added);
        Selected = added;
        MarkDirty();
    }

    private string UniqueId(string baseId)
    {
        if (Events.All(e => !string.Equals(e.Id, baseId, StringComparison.OrdinalIgnoreCase)))
            return baseId;

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseId}_{n}";
            if (Events.All(e => !string.Equals(e.Id, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void RemoveSelected()
    {
        if (Selected is not { } row)
            return;

        ((JsonArray)_root["Events"]!).Remove(row.Node);
        Events.Remove(row);
        Selected = Events.FirstOrDefault();
        MarkDirty();
    }

    private void RebuildActions()
    {
        Actions.Clear();

        if (Selected is not { } row)
            return;

        if (row.Node["Actions"] is not JsonArray actions)
            row.Node["Actions"] = actions = new JsonArray();

        foreach (var action in actions.OfType<JsonObject>())
            Actions.Add(BuildAction(action, actions));
    }

    private EventActionViewModel BuildAction(JsonObject action, JsonArray owner)
        => new(action,
            onEdited: () => { MarkDirty(); Selected?.RaiseSummary(); },
            onRemove: vm =>
            {
                owner.Remove(action);
                Actions.Remove(vm);
                MarkDirty();
                Selected?.RaiseSummary();
            },
            owner: _owner);

    private void AddAction()
    {
        if (Selected is not { } row || row.Node["Actions"] is not JsonArray actions)
            return;

        var action = new JsonObject { ["Action"] = "ShowMessage", ["Text"] = "" };
        actions.Add(action);
        Actions.Add(BuildAction(action, actions));
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

        var duplicates = Events.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            Status = $"Id repetido: {string.Join(", ", duplicates)} — o runtime só enxergaria um deles.";
            return;
        }

        // Disparo automático sem switch nunca roda (o runtime ignora de propósito). Avisa aqui,
        // porque em jogo isso é indistinguível de "o evento está quebrado".
        var semSwitch = Events.Where(e => e.Trigger != "Manual" && e.Switch.Length == 0)
            .Select(e => e.Id)
            .ToList();

        if (semSwitch.Count > 0)
        {
            Status = $"Sem switch com disparo automático: {string.Join(", ", semSwitch)} — nunca vão rodar.";
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Status = $"Salvo: {Path.GetFileName(_path)} ({Events.Count} evento(s)).";
    }
}
