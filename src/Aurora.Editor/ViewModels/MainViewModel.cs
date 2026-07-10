using System.Collections.ObjectModel;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    /// <summary>Edições com a mesma tag dentro desta janela colapsam num só passo de undo.</summary>
    private const double CoalesceWindowMs = 900;

    private SceneDocument? _document;
    private EntityViewModel? _selectedEntity;
    private bool _isDirty;
    private string _status = "Nenhuma cena aberta. Arquivo → Abrir Cena…";

    // Undo por snapshot: cada passo guarda o JSON completo da cena (cenas são pequenas).
    // _lastSnapshot é sempre o estado atual serializado — no undo ele vai para o redo.
    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private string _lastSnapshot = "";
    private string? _lastEditTag;
    private DateTime _lastEditAt;
    private bool _restoring;

    public ObservableCollection<EntityViewModel> Entities { get; } = [];
    public ObservableCollection<AssetViewModel> Assets { get; } = [];

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
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void OpenScene(string path)
    {
        _document = SceneDocument.Load(path);

        RebuildEntities();
        SelectedEntity = Entities.FirstOrDefault();

        _undoStack.Clear();
        _redoStack.Clear();
        _lastSnapshot = _document.Root.ToJsonString();
        _lastEditTag = null;

        IsDirty = false;
        Status = $"{_document.SceneName} — {Entities.Count} entidades | assets: {_document.AssetsRoot}";
        Raise(nameof(Title));
        Raise(nameof(HasDocument));
        RaiseUndoState();
        ReloadAssets();
        SceneEdited?.Invoke();
    }

    private void RebuildEntities()
    {
        Entities.Clear();
        if (_document is null)
            return;

        foreach (var objectNode in _document.Objects.OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var entity = new EntityViewModel(objectNode);
            entity.Edited += OnEdited;
            Entities.Add(entity);
        }
    }

    private static readonly string[] TextureExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>Varre a raiz de assets por texturas (para o asset browser).</summary>
    public void ReloadAssets()
    {
        Assets.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*", SearchOption.AllDirectories)
            .Where(f => TextureExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            Assets.Add(new AssetViewModel(_document.AssetsRoot, relative));
        }
    }

    /// <summary>Aplica a textura no SpriteRenderer da entidade selecionada (duplo-clique no browser).</summary>
    public void ApplyTextureToSelection(AssetViewModel asset)
    {
        var textureProperty = SelectedEntity?.Sprite?.Text("Texture");
        if (textureProperty is null)
        {
            Status = "Selecione uma entidade com SpriteRenderer para aplicar a textura.";
            return;
        }

        textureProperty.Value = asset.RelativePath;
        Status = $"{asset.RelativePath} → {SelectedEntity!.Name}";
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

    /// <summary>
    /// Cria entidade com Transform + SpriteRenderer. Sem textura vira placeholder
    /// magenta no canvas; com textura (drop do asset browser) nasce nomeada pelo arquivo.
    /// </summary>
    public void CreateEntity(double x, double y, string? texturePath = null)
    {
        if (_document is null)
            return;

        string baseName = texturePath is null
            ? "Entidade"
            : char.ToUpperInvariant(Path.GetFileNameWithoutExtension(texturePath)[0])
              + Path.GetFileNameWithoutExtension(texturePath)[1..];

        var names = Entities.Select(e => e.Name).ToHashSet();
        string name = baseName;
        for (int number = 1; names.Contains(name); number++)
            name = $"{baseName}{number}";

        var sprite = new System.Text.Json.Nodes.JsonObject { ["Type"] = "SpriteRenderer" };
        if (texturePath is not null)
            sprite["Texture"] = texturePath;

        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = name,
            ["Components"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "Transform",
                    ["X"] = (float)Math.Round(x),
                    ["Y"] = (float)Math.Round(y),
                },
                sprite),
        };

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited($"create:{node.GetHashCode()}");
    }

    public void DeleteSelectedEntity()
    {
        if (_document is null || SelectedEntity is null)
            return;

        int index = Entities.IndexOf(SelectedEntity);
        var node = SelectedEntity.Node;
        _document.Objects.Remove(node);
        Entities.Remove(SelectedEntity);

        SelectedEntity = Entities.Count > 0
            ? Entities[Math.Min(index, Entities.Count - 1)]
            : null;
        OnEdited($"delete:{node.GetHashCode()}");
    }

    // ---- Undo / Redo ----

    public void Undo()
    {
        if (_undoStack.Count == 0 || _document is null)
            return;

        _redoStack.Push(_lastSnapshot);
        Restore(_undoStack.Pop());
        Status = "Desfeito.";
    }

    public void Redo()
    {
        if (_redoStack.Count == 0 || _document is null)
            return;

        _undoStack.Push(_lastSnapshot);
        Restore(_redoStack.Pop());
        Status = "Refeito.";
    }

    private void Restore(string json)
    {
        string? selectedName = SelectedEntity?.Name;

        _restoring = true;
        _document = SceneDocument.FromJson(json, _document!.FilePath, _document.AssetsRoot);
        RebuildEntities();
        SelectedEntity = Entities.FirstOrDefault(e => e.Name == selectedName) ?? Entities.FirstOrDefault();
        _restoring = false;

        _lastSnapshot = json;
        _lastEditTag = null;
        IsDirty = true;
        RaiseUndoState();
        SceneEdited?.Invoke();
    }

    private void OnEdited(string tag)
    {
        if (_restoring || _document is null)
            return;

        bool coalesce = tag == _lastEditTag
            && (DateTime.UtcNow - _lastEditAt).TotalMilliseconds < CoalesceWindowMs;

        if (!coalesce)
        {
            _undoStack.Push(_lastSnapshot);
            _redoStack.Clear();
            RaiseUndoState();
        }

        _lastSnapshot = _document.Root.ToJsonString();
        _lastEditTag = tag;
        _lastEditAt = DateTime.UtcNow;

        IsDirty = true;
        SceneEdited?.Invoke();
    }

    private void RaiseUndoState()
    {
        Raise(nameof(CanUndo));
        Raise(nameof(CanRedo));
    }
}
