using System.Collections.ObjectModel;
using System.Diagnostics;
using Aurora.Editor.Models;

namespace Aurora.Editor.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    /// <summary>Edições com a mesma tag dentro desta janela colapsam num só passo de undo.</summary>
    private const double CoalesceWindowMs = 900;

    private SceneDocument? _document;
    private ProjectSettings? _settings;
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
    public ObservableCollection<EntityViewModel> EventEntities { get; } = [];
    public ObservableCollection<SceneFileViewModel> SceneFiles { get; } = [];
    public ObservableCollection<SceneFileViewModel> UiScreens { get; } = [];
    public ObservableCollection<PrefabFileViewModel> Prefabs { get; } = [];
    public bool HasEventEntities => EventEntities.Count > 0;

    /// <summary>Scripts [SceneScript] descobertos no projeto do jogo — alimenta o dropdown
    /// "+Add Componente" das entidades. Atualizado em background ao abrir cena/projeto.</summary>
    public ObservableCollection<GameScriptDiscovery.ScriptInfo> CustomScripts { get; } = [];

    private int _scriptCatalogVersion;

    /// <summary>Roda em background e substitui <see cref="CustomScripts"/> quando terminar.
    /// Versão incremental evita corrida entre chamadas concorrentes sobrescrevendo com resultado velho.</summary>
    private async void RefreshScriptCatalog()
    {
        if (string.IsNullOrWhiteSpace(_settings?.GameProject))
            return;

        int version = ++_scriptCatalogVersion;
        var scripts = await GameScriptDiscovery.DiscoverAsync(_settings.GameProject);
        if (version != _scriptCatalogVersion)
            return; // outra chamada mais nova já está em andamento/terminou

        CustomScripts.Clear();
        foreach (var script in scripts)
            CustomScripts.Add(script);
    }

    public SceneDocument? Document => _document;

    /// <summary>Disparado em qualquer edição — o canvas usa para redesenhar.</summary>
    public event Action? SceneEdited;

    public EntityViewModel? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (Set(ref _selectedEntity, value))
                RebuildTilePalette();
        }
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
    public bool CanPlay => _document is not null && !string.IsNullOrWhiteSpace(_settings?.GameProject);

    public string AssetsRootDisplay => _document?.AssetsRoot ?? "";

    /// <summary>Caminho do .csproj, diretório ou .exe do jogo. Salvo em aurora.project.json.</summary>
    public string GameProjectPath
    {
        get => _settings?.GameProject ?? "";
        set
        {
            if (_settings is null || _document is null)
                return;
            _settings.GameProject = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            try { _settings.Save(); } catch { /* sem permissão de escrita — ignora */ }
            Raise();
            Raise(nameof(CanPlay));
            Raise(nameof(CanBuild));
            RefreshScriptCatalog();
        }
    }

    /// <summary>Salva a cena e lança o executável ou dotnet run com --scene.</summary>
    public void Play()
    {
        if (_document is null || string.IsNullOrWhiteSpace(_settings?.GameProject))
        {
            Status = "Configure o caminho do projeto (Inspector → PROJETO) antes de usar Play.";
            return;
        }

        SaveScene();

        string project   = _settings!.GameProject!.Trim();
        string scenePath = _document.FilePath;

        try
        {
            ProcessStartInfo psi = project.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(project, $"--scene \"{scenePath}\"")
                  { UseShellExecute = true }
                : new ProcessStartInfo("dotnet",
                      $"run --project \"{project}\" -- --scene \"{scenePath}\"")
                  { UseShellExecute = true };

            Process.Start(psi);
            Status = $"Jogo iniciado — cena: {Path.GetFileName(scenePath)}";
        }
        catch (Exception ex)
        {
            Status = $"Erro ao iniciar jogo: {ex.Message}";
        }
    }

    private bool _isBuilding;
    public bool IsBuilding
    {
        get => _isBuilding;
        private set
        {
            if (Set(ref _isBuilding, value))
                Raise(nameof(CanBuild));
        }
    }

    public bool CanBuild => _document is not null && !string.IsNullOrWhiteSpace(_settings?.GameProject) && !IsBuilding;

    /// <summary>
    /// Publica o jogo self-contained (Release) pra pasta escolhida — dotnet publish por
    /// trás, mesma engrenagem que Play() já usa pra achar o projeto. Não builda plataforma
    /// diferente da que está rodando o editor (self-contained pro RID atual).
    /// </summary>
    public async Task<bool> BuildGameAsync(string outputDir)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_settings?.GameProject))
        {
            Status = "Configure o caminho do projeto (Inspector → PROJETO) antes de buildar.";
            return false;
        }

        string project = _settings!.GameProject!.Trim();
        if (project.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Status = "PROJETO aponta pra um .exe — build precisa do .csproj (ou pasta) do jogo.";
            return false;
        }

        SaveScene();
        IsBuilding = true;
        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        Status = $"Buildando ({rid}, Release)... pode levar um tempo na primeira vez.";

        try
        {
            var psi = new ProcessStartInfo("dotnet",
                $"publish \"{project}\" -c Release -r {rid} --self-contained true -o \"{outputDir}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Não consegui iniciar o dotnet publish.");

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Status = $"Build falhou (código {process.ExitCode}): {FirstErrorLine(stdout, stderr)}";
                return false;
            }

            Status = $"Build concluído: {outputDir}";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Erro ao buildar: {ex.Message}";
            return false;
        }
        finally
        {
            IsBuilding = false;
        }
    }

    private static string FirstErrorLine(string stdout, string stderr)
    {
        string combined = stderr.Length > 0 ? stderr : stdout;
        string? line = combined.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
        return line ?? "veja o log completo no terminal.";
    }

    public void ChangeAssetsRoot(string absolutePath)
    {
        if (_document is null)
            return;

        _document.SetAssetsRoot(absolutePath);
        Raise(nameof(AssetsRootDisplay));
        ReloadAssets();
        OnEdited("assetsroot");
        Status = $"Assets root: {_document.AssetsRoot}";
    }

    public void NewScene(string filePath)
    {
        _document = SceneDocument.New(filePath);
        _settings = ProjectSettings.Find(filePath);

        RebuildEntities();
        SelectedEntity = null;

        _undoStack.Clear();
        _redoStack.Clear();
        _lastSnapshot = _document.Root.ToJsonString();
        _lastEditTag = null;

        IsDirty = false;
        Status = $"Nova cena: {_document.SceneName} | assets: {_document.AssetsRoot}";
        Raise(nameof(Title));
        Raise(nameof(HasDocument));
        Raise(nameof(AssetsRootDisplay));
        Raise(nameof(CanPlay));
        Raise(nameof(CanBuild));
        Raise(nameof(GameProjectPath));
        RaiseUndoState();
        ReloadAssets();
        ReloadSceneFiles();
        ReloadPrefabs();
        ReloadUiScreens();
        RefreshScriptCatalog();
        SceneEdited?.Invoke();
    }

    public void OpenScene(string path)
    {
        _document = SceneDocument.Load(path);
        _settings = ProjectSettings.Find(path);

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
        Raise(nameof(AssetsRootDisplay));
        Raise(nameof(CanPlay));
        Raise(nameof(CanBuild));
        Raise(nameof(GameProjectPath));
        RaiseUndoState();
        ReloadAssets();
        ReloadSceneFiles();
        ReloadPrefabs();
        ReloadUiScreens();
        RefreshScriptCatalog();
        SceneEdited?.Invoke();
    }

    private void RebuildEntities()
    {
        Entities.Clear();
        if (_document is null)
        {
            RebuildEventEntities();
            return;
        }

        foreach (var objectNode in _document.Objects.OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var entity = new EntityViewModel(objectNode, this);
            entity.Edited += OnEdited;
            Entities.Add(entity);
        }
        RebuildEventEntities();
    }

    private void RebuildEventEntities()
    {
        EventEntities.Clear();
        foreach (var e in Entities.Where(e => e.HasEventTrigger))
            EventEntities.Add(e);
        Raise(nameof(HasEventEntities));
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

    /// <summary>Subpasta de destino por extensão — mesma convenção que os samples já usam
    /// (Assets/sprites, Assets/sounds, Assets/fonts). Extensão fora da lista vai pra raiz.</summary>
    private static readonly Dictionary<string, string> ImportSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "sprites", [".jpg"] = "sprites", [".jpeg"] = "sprites",
        [".wav"] = "sounds", [".ogg"] = "sounds",
        [".ttf"] = "fonts",
    };

    /// <summary>
    /// Copia arquivos externos pra dentro da pasta de assets do projeto (subpasta por tipo)
    /// e recarrega o painel ASSETS — sem precisar sair pro Explorer e copiar na mão.
    /// Não sobrescreve: em conflito de nome, renomeia com sufixo numérico.
    /// </summary>
    public void ImportAssets(IEnumerable<string> sourcePaths)
    {
        if (_document is null)
            return;

        int imported = 0;
        foreach (string source in sourcePaths)
        {
            string ext = Path.GetExtension(source);
            string destDir = ImportSubfolders.TryGetValue(ext, out var subfolder)
                ? Path.Combine(_document.AssetsRoot, subfolder)
                : _document.AssetsRoot;

            Directory.CreateDirectory(destDir);
            string destPath = UniquePath(Path.Combine(destDir, Path.GetFileName(source)));
            File.Copy(source, destPath);
            imported++;
        }

        ReloadAssets();
        Status = imported == 1 ? "1 asset importado." : $"{imported} assets importados.";
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>Varre a raiz de assets por cenas .json (para o painel CENAS). Cena tem "Objects"
    /// na raiz sem marca "UI" — tela de UI (mesma pasta) tem "Objects" + "UI":true; prefab tem
    /// "Components" na raiz sem "Objects". As três nunca se confundem.</summary>
    public void ReloadSceneFiles()
    {
        SceneFiles.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => LooksLikeScene(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            SceneFiles.Add(new SceneFileViewModel(file, relative)
            {
                IsCurrent = string.Equals(Path.GetFullPath(file), Path.GetFullPath(_document.FilePath),
                    StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    /// <summary>Varre a raiz de assets por telas de UI .json (para o painel TELAS UI). Mesmo
    /// formato de cena (Objects/Components), com componentes UiText/UiImage/UiBar/UiPanel em
    /// pixels de tela — persistem entre trocas de cena no runtime (ver Aurora.Runtime.UI.UIManager).</summary>
    public void ReloadUiScreens()
    {
        UiScreens.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => LooksLikeUiScreen(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            UiScreens.Add(new SceneFileViewModel(file, relative)
            {
                IsCurrent = string.Equals(Path.GetFullPath(file), Path.GetFullPath(_document.FilePath),
                    StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    /// <summary>Varre a raiz de assets por prefabs .json (para o painel PREFABS). Prefab tem
    /// "Components" na raiz sem "Objects" — o oposto de uma cena.</summary>
    public void ReloadPrefabs()
    {
        Prefabs.Clear();
        if (_document is null || !Directory.Exists(_document.AssetsRoot))
            return;

        var files = Directory.EnumerateFiles(_document.AssetsRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => LooksLikePrefab(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(_document.AssetsRoot, file).Replace('\\', '/');
            Prefabs.Add(new PrefabFileViewModel(file, relative));
        }
    }

    private static bool LooksLikeScene(string jsonPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("Objects", out _) && !IsUiMarked(doc.RootElement);
        }
        catch { return false; }
    }

    private static bool LooksLikeUiScreen(string jsonPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("Objects", out _) && IsUiMarked(doc.RootElement);
        }
        catch { return false; }
    }

    private static bool IsUiMarked(System.Text.Json.JsonElement root)
        => root.TryGetProperty("UI", out var ui) && ui.ValueKind == System.Text.Json.JsonValueKind.True;

    private static bool LooksLikePrefab(string jsonPath)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
            return doc.RootElement.TryGetProperty("Components", out _)
                && !doc.RootElement.TryGetProperty("Objects", out _);
        }
        catch { return false; }
    }

    /// <summary>Cria uma tela de UI vazia (mesmo formato de cena, com a marca "UI":true) e
    /// já abre pra edição — reusa toda a maquinaria de OpenScene (hierarquia, inspector,
    /// undo/redo funcionam sem nenhum código a mais).</summary>
    public void NewUiScreen(string filePath)
    {
        var root = new System.Text.Json.Nodes.JsonObject
        {
            ["Scene"] = Path.GetFileNameWithoutExtension(filePath),
            ["UI"] = true,
            ["Objects"] = new System.Text.Json.Nodes.JsonArray(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath,
            root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        OpenScene(filePath);
    }

    /// <summary>Troca de cena a partir do painel CENAS. Salva a atual antes, se suja (sem
    /// diálogo de confirmação — mesmo comportamento silencioso que Play() já usa).</summary>
    public void OpenSceneFile(SceneFileViewModel file)
    {
        if (file.IsCurrent)
            return;

        if (IsDirty)
            SaveScene();

        OpenScene(file.FullPath);
    }

    /// <summary>Aplica a textura no SpriteRenderer (ou Tilemap) da entidade selecionada.</summary>
    public void ApplyTextureToSelection(AssetViewModel asset)
    {
        var textureProperty = SelectedEntity?.Sprite?.Text("Texture")
            ?? SelectedEntity?.Tilemap?.Text("Texture");
        if (textureProperty is null)
        {
            Status = "Selecione uma entidade com SpriteRenderer ou Tilemap para aplicar a textura.";
            return;
        }

        textureProperty.Value = asset.RelativePath;
        Status = $"{asset.RelativePath} → {SelectedEntity!.Name}";
    }

    // ---- Paleta de tiles ----

    private int? _selectedTileIndex;
    private TileBrushViewModel? _selectedTileBrush;
    private string? _paletteSignature;

    public ObservableCollection<TileBrushViewModel> PaletteTiles { get; } = [];

    public bool HasTilePalette => PaletteTiles.Count > 0;

    /// <summary>Tile ativo para pintura no canvas. Null = modo seleção/movimento normal.</summary>
    public int? SelectedTileIndex
    {
        get => _selectedTileIndex;
        private set => Set(ref _selectedTileIndex, value);
    }

    /// <summary>Item selecionado na paleta (binding do ListBox).</summary>
    public TileBrushViewModel? SelectedTileBrush
    {
        get => _selectedTileBrush;
        set
        {
            if (Set(ref _selectedTileBrush, value))
                SelectedTileIndex = value?.Index;
        }
    }

    /// <summary>Sai do modo pintura (Escape).</summary>
    public void ClearTileBrush() => SelectedTileBrush = null;

    /// <summary>Monta a paleta a partir do tileset da entidade selecionada.</summary>
    private void RebuildTilePalette()
    {
        var map = SelectedEntity?.Tilemap;
        string? texture = map?.GetString("Texture");
        int tileWidth = (int)(map?.GetFloat("TileWidth", 16f) ?? 16);
        int tileHeight = (int)(map?.GetFloat("TileHeight", 16f) ?? 16);

        string? signature = map is null || texture is null
            ? null
            : $"{texture}|{tileWidth}|{tileHeight}";

        if (signature == _paletteSignature)
            return;

        _paletteSignature = signature;
        SelectedTileBrush = null;
        PaletteTiles.Clear();

        if (signature is not null && _document is not null && tileWidth > 0 && tileHeight > 0)
        {
            string fullPath = Path.Combine(_document.AssetsRoot, texture!);
            if (File.Exists(fullPath))
            {
                var bitmap = new Avalonia.Media.Imaging.Bitmap(fullPath);
                int columns = Math.Max(1, (int)bitmap.PixelSize.Width / tileWidth);
                int rows = Math.Max(1, (int)bitmap.PixelSize.Height / tileHeight);

                PaletteTiles.Add(new TileBrushViewModel(-1, null));

                for (int index = 0; index < columns * rows; index++)
                {
                    var source = new Avalonia.PixelRect(
                        index % columns * tileWidth, index / columns * tileHeight, tileWidth, tileHeight);
                    PaletteTiles.Add(new TileBrushViewModel(index,
                        new Avalonia.Media.Imaging.CroppedBitmap(bitmap, source)));
                }
            }
        }

        Raise(nameof(HasTilePalette));
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
        ReloadSceneFiles();
        ReloadUiScreens();
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

        var entity = new EntityViewModel(node, this);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited($"create:{node.GetHashCode()}");
    }

    /// <summary>Cria um tilemap 20x15 (tiles 16px) centrado no ponto dado, sem tileset.</summary>
    public void CreateTilemap(double x, double y)
    {
        if (_document is null)
            return;

        var names = Entities.Select(e => e.Name).ToHashSet();
        string name = "Tilemap";
        for (int number = 1; names.Contains(name); number++)
            name = $"Tilemap{number}";

        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = name,
            ["Components"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "Transform",
                    ["X"] = (float)Math.Round(x - 160),
                    ["Y"] = (float)Math.Round(y - 120),
                },
                new System.Text.Json.Nodes.JsonObject
                {
                    ["Type"] = "Tilemap",
                    ["TileWidth"] = 16,
                    ["TileHeight"] = 16,
                    ["Width"] = 20,
                    ["Height"] = 15,
                    ["Tiles"] = new System.Text.Json.Nodes.JsonArray(),
                }),
        };

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node, this);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited($"create:{node.GetHashCode()}");
        Status = $"{name} criado — defina o tileset (duplo-clique num asset) e pinte.";
    }

    /// <summary>Instancia uma prefab na cena atual: clona os Components do arquivo, dá um
    /// Transform novo na posição pedida e linka a entidade à prefab (duplo-clique no painel PREFABS).</summary>
    public void CreatePrefabInstance(PrefabFileViewModel prefab, double x, double y)
    {
        if (_document is null)
            return;

        if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(prefab.FullPath))
                is not System.Text.Json.Nodes.JsonObject prefabRoot
            || prefabRoot["Components"] is not System.Text.Json.Nodes.JsonArray prefabComponents)
        {
            Status = $"Prefab '{prefab.Name}' inválida — sem 'Components'.";
            return;
        }

        var names = Entities.Select(e => e.Name).ToHashSet();
        string name = prefab.Name;
        for (int number = 1; names.Contains(name); number++)
            name = $"{prefab.Name}{number}";

        var components = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject
            {
                ["Type"] = "Transform",
                ["X"] = (float)Math.Round(x),
                ["Y"] = (float)Math.Round(y),
            });
        foreach (var comp in prefabComponents)
        {
            if (comp is System.Text.Json.Nodes.JsonObject obj && obj["Type"]?.GetValue<string>() == "Transform")
                continue;
            components.Add(System.Text.Json.Nodes.JsonNode.Parse(comp!.ToJsonString()));
        }

        var node = new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = name,
            ["Prefab"] = prefab.RelativePath,
            ["Components"] = components,
        };

        _document.Objects.Add(node);

        var entity = new EntityViewModel(node, this);
        entity.Edited += OnEdited;
        Entities.Add(entity);
        SelectedEntity = entity;
        OnEdited($"create:{node.GetHashCode()}");
        Status = $"{name} instanciada de {prefab.Name}.";
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

        if (tag.StartsWith("addcomp:") || tag.StartsWith("removecomp:"))
            RebuildEventEntities();

        IsDirty = true;
        RebuildTilePalette();
        SceneEdited?.Invoke();
    }

    private void RaiseUndoState()
    {
        Raise(nameof(CanUndo));
        Raise(nameof(CanRedo));
    }
}
