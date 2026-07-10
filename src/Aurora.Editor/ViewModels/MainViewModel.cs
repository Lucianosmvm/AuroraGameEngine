using System.Collections.ObjectModel;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private SceneDocument? _document;
    private EntityViewModel? _selectedEntity;
    private bool _isDirty;
    private string _status = "Nenhuma cena aberta. Arquivo → Abrir Cena…";

    public ObservableCollection<EntityViewModel> Entities { get; } = [];

    public SceneDocument? Document => _document;

    /// <summary>Disparado em qualquer edição — o canvas usa para redesenhar.</summary>
    public event Action? SceneEdited;

    public EntityViewModel? SelectedEntity
    {
        get => _selectedEntity;
        set => Set(ref _selectedEntity, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
                Raise(nameof(Title));
        }
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string Title => _document is null
        ? "Aurora Editor"
        : $"Aurora Editor — {Path.GetFileName(_document.FilePath)}{(IsDirty ? " *" : "")}";

    public bool HasDocument => _document is not null;

    public void OpenScene(string path)
    {
        _document = SceneDocument.Load(path);

        Entities.Clear();
        foreach (var objectNode in _document.Objects.OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var entity = new EntityViewModel(objectNode);
            entity.Edited += OnEdited;
            Entities.Add(entity);
        }

        SelectedEntity = Entities.FirstOrDefault();
        IsDirty = false;
        Status = $"{_document.SceneName} — {Entities.Count} entidades | assets: {_document.AssetsRoot}";
        Raise(nameof(Title));
        Raise(nameof(HasDocument));
        SceneEdited?.Invoke();
    }

    public void SaveScene()
    {
        if (_document is null)
            return;

        _document.Save();
        IsDirty = false;
        Status = $"Salvo: {_document.FilePath}";
    }

    public void SaveSceneAs(string path)
    {
        if (_document is null)
            return;

        _document.Save(path);
        IsDirty = false;
        Raise(nameof(Title));
        Status = $"Salvo: {path}";
    }

    /// <summary>Cria entidade com Transform + SpriteRenderer vazio (placeholder magenta no canvas).</summary>
    public void CreateEntity(double x, double y)
    {
        if (_document is null)
            return;

        var names = Entities.Select(e => e.Name).ToHashSet();
        int number = 1;
        while (names.Contains($"Entidade{number}"))
            number++;

        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = $"Entidade{number}",
            ["Components"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "Transform",
                    ["X"] = (float)Math.Round(x),
                    ["Y"] = (float)Math.Round(y),
                },
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "SpriteRenderer",
                }),
        };

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited();
    }

    public void DeleteSelectedEntity()
    {
        if (_document is null || SelectedEntity is null)
            return;

        int index = Entities.IndexOf(SelectedEntity);
        _document.Objects.Remove(SelectedEntity.Node);
        Entities.Remove(SelectedEntity);

        SelectedEntity = Entities.Count > 0
            ? Entities[Math.Min(index, Entities.Count - 1)]
            : null;
        OnEdited();
    }

    private void OnEdited()
    {
        IsDirty = true;
        SceneEdited?.Invoke();
    }

    /// <summary>Usado pelo canvas ao arrastar entidades.</summary>
    public void NotifyEdited() => OnEdited();
}
