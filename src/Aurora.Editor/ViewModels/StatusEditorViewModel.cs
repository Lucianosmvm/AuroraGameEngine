using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

/// <summary>Uma ficha de efeito de status: quanto dura e o que ele muda em quem está com ele.</summary>
public sealed class StatusRowViewModel : ViewModelBase
{
    private readonly Action _onEdited;

    public JsonObject Node { get; }

    public StatusRowViewModel(JsonObject node, Action onEdited)
    {
        Node = node;
        _onEdited = onEdited;
    }

    public string Id
    {
        get => Node["Id"]?.GetValue<string>() ?? "";
        set { Node["Id"] = value; Raise(); Raise(nameof(Display)); _onEdited(); }
    }

    public string Name
    {
        get => Node["Name"]?.GetValue<string>() ?? "";
        set { SetText("Name", value); Raise(); Raise(nameof(Display)); }
    }

    public string Icon
    {
        get => Node["Icon"]?.GetValue<string>() ?? "";
        set { SetText("Icon", value); Raise(); }
    }

    /// <summary>Segundos. 0 = permanente até alguém remover.</summary>
    public string DurationText
    {
        get => Number("Duration", 0f);
        set { SetNumber("Duration", value, 0f); Raise(); Raise(nameof(Summary)); }
    }

    /// <summary>Positivo machuca, negativo cura. Um campo só pra veneno e regeneração.</summary>
    public string DamagePerSecondText
    {
        get => Number("DamagePerSecond", 0f);
        set { SetNumber("DamagePerSecond", value, 0f); Raise(); Raise(nameof(Summary)); }
    }

    public string SpeedMultiplierText
    {
        get => Number("SpeedMultiplier", 1f);
        set { SetNumber("SpeedMultiplier", value, 1f); Raise(); Raise(nameof(Summary)); }
    }

    public string DamageTakenMultiplierText
    {
        get => Number("DamageTakenMultiplier", 1f);
        set { SetNumber("DamageTakenMultiplier", value, 1f); Raise(); Raise(nameof(Summary)); }
    }

    /// <summary>Reaplicar renova a duração (padrão) ou é ignorado.</summary>
    public bool RefreshOnReapply
    {
        get => Node["RefreshOnReapply"]?.GetValue<bool>() ?? true;
        set
        {
            if (value) Node.Remove("RefreshOnReapply");
            else Node["RefreshOnReapply"] = false;
            Raise();
            _onEdited();
        }
    }

    public string Display => Name.Length > 0 ? $"{Name}  ({Id})" : Id;

    /// <summary>Resumo em português do que a ficha faz — ler "4/s por 5s, velocidade 0.5" é mais
    /// rápido que abrir cada cadastro pra lembrar qual era o veneno forte.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            float dps = Node["DamagePerSecond"].AsFloat(0f);
            if (dps > 0f) parts.Add($"{dps:0.##} dano/s");
            else if (dps < 0f) parts.Add($"{-dps:0.##} cura/s");

            float speed = Node["SpeedMultiplier"].AsFloat(1f);
            if (Math.Abs(speed - 1f) > 0.001f) parts.Add($"velocidade ×{speed:0.##}");

            float taken = Node["DamageTakenMultiplier"].AsFloat(1f);
            if (Math.Abs(taken - 1f) > 0.001f) parts.Add($"dano recebido ×{taken:0.##}");

            float duration = Node["Duration"].AsFloat(0f);
            parts.Add(duration > 0f ? $"{duration:0.##}s" : "permanente");

            return string.Join(" · ", parts);
        }
    }

    private void SetText(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) Node.Remove(key);
        else Node[key] = value;
        _onEdited();
    }

    private string Number(string key, float fallback)
        => Node[key].AsFloat(fallback).ToString(CultureInfo.InvariantCulture);

    private void SetNumber(string key, string text, float omitWhen)
    {
        if (!float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            return;

        // Valor igual ao padrão sai do JSON: ficha nova não nasce com seis campos que não fazem
        // nada, e quem abre o arquivo vê só o que aquele status realmente muda.
        if (Math.Abs(parsed - omitWhen) < 0.0001f) Node.Remove(key);
        else Node[key] = parsed;

        Raise(nameof(Summary));
        _onEdited();
    }
}

/// <summary>
/// Aba "Status" do banco. Edita <c>Assets/database/status.json</c>: veneno, lentidão, blindagem —
/// efeitos temporários aplicados pelas ações AddStatus/RemoveStatus.
///
/// <para>Nada disso é obrigatório: sem o arquivo, o jogo roda igual e as ações só avisam no
/// console se alguém chamar um id que não existe.</para>
/// </summary>
public sealed class StatusEditorViewModel : ViewModelBase
{
    private readonly string _path;
    private JsonObject _root = new();

    public ObservableCollection<StatusRowViewModel> Rows { get; } = [];

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand DuplicateCommand { get; }

    private StatusRowViewModel? _selected;
    public StatusRowViewModel? Selected
    {
        get => _selected;
        set { _selected = value; Raise(); Raise(nameof(HasSelection)); }
    }

    public bool HasSelection => _selected is not null;

    private string _status = "";
    public string Status
    {
        get => _status;
        private set { _status = value; Raise(); }
    }

    public StatusEditorViewModel(string jsonPath)
    {
        _path = jsonPath;
        AddCommand = new RelayCommand(AddStatus);
        RemoveCommand = new RelayCommand(RemoveSelected);
        DuplicateCommand = new RelayCommand(DuplicateSelected);
        Load();
    }

    private void Load()
    {
        Rows.Clear();

        if (_path.Length == 0 || !File.Exists(_path))
        {
            _root = new JsonObject { ["Status"] = new JsonArray() };
            Status = "Nenhum status ainda.";
            return;
        }

        try
        {
            _root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                    ?? new JsonObject { ["Status"] = new JsonArray() };
        }
        catch (Exception ex)
        {
            _root = new JsonObject { ["Status"] = new JsonArray() };
            Status = $"status.json inválido ({ex.Message}) — não salve por cima sem conferir.";
            return;
        }

        if (_root["Status"] is not JsonArray array)
            _root["Status"] = array = new JsonArray();

        foreach (var node in array.OfType<JsonObject>())
            Rows.Add(new StatusRowViewModel(node, MarkDirty));

        Status = $"{Rows.Count} status.";
    }

    private void MarkDirty() => Status = "Alterado — clique em Salvar.";

    private void AddStatus()
    {
        var node = new JsonObject { ["Id"] = UniqueId("status_novo"), ["Duration"] = 5f };
        ((JsonArray)_root["Status"]!).Add(node);

        var row = new StatusRowViewModel(node, MarkDirty);
        Rows.Add(row);
        Selected = row;
        MarkDirty();
    }

    private void DuplicateSelected()
    {
        if (Selected is not { } row || row.Node.DeepClone() is not JsonObject copy)
            return;

        copy["Id"] = UniqueId($"{row.Id}_copia");
        ((JsonArray)_root["Status"]!).Add(copy);

        var added = new StatusRowViewModel(copy, MarkDirty);
        Rows.Add(added);
        Selected = added;
        MarkDirty();
    }

    private string UniqueId(string baseId)
    {
        if (Rows.All(r => !string.Equals(r.Id, baseId, StringComparison.OrdinalIgnoreCase)))
            return baseId;

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseId}_{n}";
            if (Rows.All(r => !string.Equals(r.Id, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void RemoveSelected()
    {
        if (Selected is not { } row)
            return;

        ((JsonArray)_root["Status"]!).Remove(row.Node);
        Rows.Remove(row);
        Selected = Rows.FirstOrDefault();
        MarkDirty();
    }

    public void Save()
    {
        if (_path.Length == 0)
        {
            Status = "Sem projeto aberto — nada a salvar.";
            return;
        }

        var duplicates = Rows.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
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
        Status = $"Salvo: {Path.GetFileName(_path)} ({Rows.Count} status).";
    }
}
