using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

/// <summary>Uma lista de categorias: um id e os valores permitidos.</summary>
public sealed class TypeListViewModel : ViewModelBase
{
    private readonly Action _onEdited;

    public JsonObject Node { get; }

    public TypeListViewModel(JsonObject node, Action onEdited)
    {
        Node = node;
        _onEdited = onEdited;
    }

    public string Id
    {
        get => Node["Id"]?.GetValue<string>() ?? "";
        set { Node["Id"] = value; Raise(); Raise(nameof(Display)); _onEdited(); }
    }

    /// <summary>
    /// Os valores, um por linha. Caixa de texto e não lista de linhas com botão de "+": cadastrar
    /// oito categorias é digitar oito palavras, e um formulário por palavra transformaria dois
    /// minutos de trabalho em vinte cliques.
    /// </summary>
    public string ValuesText
    {
        get => Node["Values"] is JsonArray array
            ? string.Join(Environment.NewLine, array.Select(v => v?.GetValue<string>() ?? ""))
            : "";
        set
        {
            var array = new JsonArray();
            foreach (string line in value.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                                         .Select(l => l.Trim())
                                         .Where(l => l.Length > 0))
                array.Add(line);

            Node["Values"] = array;
            Raise();
            Raise(nameof(Summary));
            _onEdited();
        }
    }

    public string Display => Id;

    public string Summary
    {
        get
        {
            if (Node["Values"] is not JsonArray array || array.Count == 0)
                return "vazia — aceita qualquer texto";

            string preview = string.Join(", ", array.Take(4).Select(v => v?.GetValue<string>() ?? ""));
            return array.Count > 4 ? $"{preview}… ({array.Count})" : preview;
        }
    }

    /// <summary>ItemTypes é a única lista que a engine consulta sozinha (confere o campo Tipo dos
    /// itens no boot). As outras existem pros scripts do jogo — o editor avisa qual é qual pra
    /// ninguém esperar validação de uma lista que só o jogo entende.</summary>
    public bool IsEngineList => Id.Equals("ItemTypes", StringComparison.OrdinalIgnoreCase);

    public string ListHint => IsEngineList
        ? "A engine confere o campo Tipo dos itens contra esta lista e avisa no console quem estiver fora."
        : "Lista livre: o editor sugere os valores, e os scripts do seu jogo leem pelo id.";
}

/// <summary>
/// Aba "Tipos" do banco. Edita <c>Assets/database/types.json</c>: listas de categorias do jogo.
///
/// <para>Existe por um motivo pequeno e caro: campo de categoria digitado à mão vira "Consumivel"
/// numa ficha e "Consumível" na outra, e o jogo filtra errado sem avisar. Cadastrando, o editor
/// sugere e o jogo reclama no boot.</para>
///
/// <para>Nenhuma categoria vem pronta — quem decide se o jogo tem "Arma" ou "Peça de motor" é o
/// jogo. Sem o arquivo, todo campo continua aceitando texto livre, como sempre aceitou.</para>
/// </summary>
public sealed class TypeEditorViewModel : ViewModelBase
{
    private readonly string _path;
    private JsonObject _root = new();

    public ObservableCollection<TypeListViewModel> Lists { get; } = [];

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand AddItemTypesCommand { get; }

    private TypeListViewModel? _selected;
    public TypeListViewModel? Selected
    {
        get => _selected;
        set { _selected = value; Raise(); Raise(nameof(HasSelection)); }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>Só oferece o atalho de criar a ItemTypes quando ela ainda não existe.</summary>
    public bool CanAddItemTypes =>
        Lists.All(l => !l.Id.Equals("ItemTypes", StringComparison.OrdinalIgnoreCase));

    private string _status = "";
    public string Status
    {
        get => _status;
        private set { _status = value; Raise(); }
    }

    public TypeEditorViewModel(string jsonPath)
    {
        _path = jsonPath;
        AddCommand = new RelayCommand(AddList);
        RemoveCommand = new RelayCommand(RemoveSelected);
        AddItemTypesCommand = new RelayCommand(AddItemTypes);
        Load();
    }

    private void Load()
    {
        Lists.Clear();

        if (_path.Length == 0 || !File.Exists(_path))
        {
            _root = new JsonObject { ["Types"] = new JsonArray() };
            Status = "Nenhuma lista ainda.";
            return;
        }

        try
        {
            _root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                    ?? new JsonObject { ["Types"] = new JsonArray() };
        }
        catch (Exception ex)
        {
            _root = new JsonObject { ["Types"] = new JsonArray() };
            Status = $"types.json inválido ({ex.Message}) — não salve por cima sem conferir.";
            return;
        }

        if (_root["Types"] is not JsonArray array)
            _root["Types"] = array = new JsonArray();

        foreach (var node in array.OfType<JsonObject>())
            Lists.Add(new TypeListViewModel(node, MarkDirty));

        Status = $"{Lists.Count} lista(s).";
    }

    private void MarkDirty() => Status = "Alterado — clique em Salvar.";

    private void AddList() => Create(UniqueId("lista_nova"));

    /// <summary>Cria a ItemTypes já preenchida com as categorias mais comuns. É um empurrão, não
    /// uma regra: dá pra apagar tudo e escrever as suas.</summary>
    private void AddItemTypes()
    {
        var node = Create("ItemTypes");
        node.ValuesText = string.Join(Environment.NewLine, "Consumivel", "Arma", "Armadura", "Material", "Chave");
    }

    private TypeListViewModel Create(string id)
    {
        var node = new JsonObject { ["Id"] = id, ["Values"] = new JsonArray() };
        ((JsonArray)_root["Types"]!).Add(node);

        var row = new TypeListViewModel(node, MarkDirty);
        Lists.Add(row);
        Selected = row;
        Raise(nameof(CanAddItemTypes));
        MarkDirty();
        return row;
    }

    private string UniqueId(string baseId)
    {
        if (Lists.All(l => !string.Equals(l.Id, baseId, StringComparison.OrdinalIgnoreCase)))
            return baseId;

        for (int n = 2; ; n++)
        {
            string candidate = $"{baseId}_{n}";
            if (Lists.All(l => !string.Equals(l.Id, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void RemoveSelected()
    {
        if (Selected is not { } row)
            return;

        ((JsonArray)_root["Types"]!).Remove(row.Node);
        Lists.Remove(row);
        Selected = Lists.FirstOrDefault();
        Raise(nameof(CanAddItemTypes));
        MarkDirty();
    }

    public void Save()
    {
        if (_path.Length == 0)
        {
            Status = "Sem projeto aberto — nada a salvar.";
            return;
        }

        var duplicates = Lists.GroupBy(l => l.Id, StringComparer.OrdinalIgnoreCase)
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
        Status = $"Salvo: {Path.GetFileName(_path)} ({Lists.Count} lista(s)).";
    }
}
