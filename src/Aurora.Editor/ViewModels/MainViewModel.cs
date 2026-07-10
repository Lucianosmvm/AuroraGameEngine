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

    private void OnEdited()
    {
        IsDirty = true;
        SceneEdited?.Invoke();
    }

    /// <summary>Usado pelo canvas ao arrastar entidades.</summary>
    public void NotifyEdited() => OnEdited();
}
