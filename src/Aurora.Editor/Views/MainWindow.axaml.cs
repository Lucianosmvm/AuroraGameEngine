using System.Diagnostics;
using Aurora.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Aurora.Editor.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    private ViewModels.AssetViewModel? _dragCandidate;
    private Avalonia.Point _dragStart;

    public MainWindow()
    {
        InitializeComponent();

        // O seletor de arquivo do sistema mora na janela (é ela que tem o StorageProvider), mas
        // quem precisa dele é o campo de textura lá no inspector — a VM guarda só o gancho.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.PickTextureFromDisk = PickTextureAsync;
        };

        // Arrastar asset para o canvas: só vira drag depois de 8px de movimento,
        // senão o clique de seleção na lista seria engolido.
        AssetList.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(AssetList).Properties.IsLeftButtonPressed)
            {
                _dragCandidate = (e.Source as Control)?.DataContext as ViewModels.AssetViewModel;
                _dragStart = e.GetPosition(AssetList);
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        AssetList.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (_dragCandidate is null || !e.GetCurrentPoint(AssetList).Properties.IsLeftButtonPressed)
                return;

            var delta = e.GetPosition(AssetList) - _dragStart;
            if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8)
                return;

            // Obsoleto no 11.3, funcional no 11.x — migrar junto com Avalonia 12 (ver SceneCanvas).
#pragma warning disable CS0618
            var data = new DataObject();
            data.Set(DataFormats.Text, _dragCandidate.RelativePath);
            _dragCandidate = null;
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
#pragma warning restore CS0618
        });

        AssetList.AddHandler(PointerReleasedEvent, (_, _) => _dragCandidate = null,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        KeyDown += (_, e) =>
        {
            if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.S)
            {
                _ = PickAndSaveSceneAsAsync();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S)
            {
                ViewModel.SaveScene();
                e.Handled = true;
            }
            else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.N)
            {
                _ = PickAndNewProjectAsync();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.N)
            {
                _ = PickAndNewSceneAsync();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O)
            {
                _ = PickAndOpenSceneAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ViewModel.ClearTileBrush();
                e.Handled = true;
            }
            else if (e.Key == Key.Home && e.Source is not TextBox)
            {
                // Zoom do scroll não tem limite visual (só o clamp interno) — rolar demais
                // deixa a cena minúscula/fora de vista. Home restaura câmera/zoom padrão.
                Scene.ResetView();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && e.Source is not TextBox)
            {
                // Delete só fora de campos de texto — senão apagar caractere apaga entidade.
                ViewModel.DeleteSelectedEntity();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z && e.Source is not TextBox)
            {
                ViewModel.Undo();
                e.Handled = true;
            }
            else if (e.Source is not TextBox
                && (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Y
                    || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key == Key.Z))
            {
                ViewModel.Redo();
                e.Handled = true;
            }
        };
    }

    private async Task PickAndNewProjectAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Novo projeto — nome do jogo",
            DefaultExtension = "csproj",
            SuggestedFileName = "MeuJogo.csproj",
            FileTypeChoices =
            [
                new FilePickerFileType("Projeto Aurora") { Patterns = ["*.csproj"] },
            ],
        });

        if (file?.TryGetLocalPath() is not { } path)
            return;

        string parent = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string projectDir = Path.Combine(parent, name);

        try
        {
            string scenePath = Models.GameProjectScaffolder.Create(projectDir, name);
            ViewModel.OpenScene(scenePath);
            ViewModel.Status = $"Projeto criado em {projectDir}";
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"Erro ao criar projeto: {ex.Message}";
        }
    }

    private async Task PickAndNewSceneAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Nova cena — escolha onde salvar",
            DefaultExtension = "json",
            SuggestedFileName = "novacena.json",
            FileTypeChoices =
            [
                new FilePickerFileType("Cena Aurora (JSON)") { Patterns = ["*.json"] },
            ],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            try
            {
                ViewModel.NewScene(path);
            }
            catch (Exception ex)
            {
                ViewModel.Status = $"Erro ao criar cena: {ex.Message}";
            }
        }
    }

    private async Task PickAndNewUiScreenAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Nova tela de UI — escolha onde salvar",
            DefaultExtension = "json",
            SuggestedFileName = "hud.json",
            FileTypeChoices =
            [
                new FilePickerFileType("Tela de UI Aurora (JSON)") { Patterns = ["*.json"] },
            ],
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            try
            {
                ViewModel.NewUiScreen(path);
            }
            catch (Exception ex)
            {
                ViewModel.Status = $"Erro ao criar tela de UI: {ex.Message}";
            }
        }
    }

    private async Task PickAndOpenSceneAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir cena Aurora",
            FileTypeFilter =
            [
                new FilePickerFileType("Cena Aurora (JSON)") { Patterns = ["*.json"] },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            try
            {
                ViewModel.OpenScene(path);
            }
            catch (Exception ex)
            {
                ViewModel.Status = $"Erro ao abrir cena: {ex.Message}";
            }
        }
    }

    private async Task PickAndSaveSceneAsAsync()
    {
        if (ViewModel.Document is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar cena como",
            DefaultExtension = "json",
            SuggestedFileName = Path.GetFileName(ViewModel.Document.FilePath),
            FileTypeChoices =
            [
                new FilePickerFileType("Cena Aurora (JSON)") { Patterns = ["*.json"] },
            ],
        });

        if (file?.TryGetLocalPath() is { } path)
            ViewModel.SaveSceneAs(path);
    }

    private void OnNewProject(object? sender, RoutedEventArgs e) => _ = PickAndNewProjectAsync();

    private void OnOpenProject(object? sender, RoutedEventArgs e) => _ = PickAndOpenProjectAsync();

    private async Task PickAndOpenProjectAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Abrir projeto Aurora (pasta com aurora.project.json)",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            try
            {
                ViewModel.OpenProject(path);
            }
            catch (Exception ex)
            {
                ViewModel.Status = $"Erro ao abrir projeto: {ex.Message}";
            }
        }
    }

    private void OnNewScene(object? sender, RoutedEventArgs e) => _ = PickAndNewSceneAsync();

    private void OnOpenScene(object? sender, RoutedEventArgs e) => _ = PickAndOpenSceneAsync();

    private void OnSaveScene(object? sender, RoutedEventArgs e) => ViewModel.SaveScene();

    private void OnSaveSceneAs(object? sender, RoutedEventArgs e) => _ = PickAndSaveSceneAsAsync();

    private void OnCreateEntity(object? sender, RoutedEventArgs e)
    {
        var center = Scene.CameraCenter;
        ViewModel.CreateEntity(center.X, center.Y);
    }

    private void OnCreateTilemap(object? sender, RoutedEventArgs e)
    {
        var center = Scene.CameraCenter;
        ViewModel.CreateTilemap(center.X, center.Y);
    }

    private void OnDeleteEntity(object? sender, RoutedEventArgs e) => ViewModel.DeleteSelectedEntity();

    private void OnUndo(object? sender, RoutedEventArgs e) => ViewModel.Undo();

    private void OnRedo(object? sender, RoutedEventArgs e) => ViewModel.Redo();

    /// <summary>Abre (ou traz pra frente) a janela de Configurações do Projeto. Uma só instância,
    /// porque abrir duas deixaria os mesmos campos editáveis em dois lugares.</summary>
    private void OnOpenProjectSettings(object? sender, RoutedEventArgs e)
    {
        if (_projectSettings is not null)
        {
            _projectSettings.Activate();
            return;
        }

        var window = new ProjectSettingsWindow(ViewModel);
        window.AssetsRootChanged += () => Scene.ClearTextureCache();
        window.Closed += (_, _) => _projectSettings = null;
        _projectSettings = window;
        window.Show(this);
    }

    private ProjectSettingsWindow? _projectSettings;

    /// <summary>
    /// Abre o banco de dados de itens do projeto. Janela própria e não-modal, igual às
    /// configurações: cadastrar item é uma tarefa longa que se faz junto com a cena aberta.
    /// </summary>
    private void OnOpenItemDatabase(object? sender, RoutedEventArgs e)
    {
        if (_itemDatabase is not null)
        {
            _itemDatabase.Activate();
            return;
        }

        var window = new DatabaseWindow(new ViewModels.DatabaseViewModel(ViewModel));
        window.Closed += (_, _) => _itemDatabase = null;
        _itemDatabase = window;
        window.Show(this);
    }

    private DatabaseWindow? _itemDatabase;

    private void OnPlay(object? sender, RoutedEventArgs e) => ViewModel.Play();

    private async Task PickAndBuildAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Build Jogo — escolha a pasta de saída",
            AllowMultiple = false,
        });

        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } outputDir)
            return;

        bool ok = await ViewModel.BuildGameAsync(outputDir);
        if (ok)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{outputDir}\"")); }
            catch { /* abrir o Explorer é conveniência, não impede o build ter dado certo */ }
        }
    }

    private void OnBuildGame(object? sender, RoutedEventArgs e) => _ = PickAndBuildAsync();

    private async Task PickAndExportAndroidAsync()
    {
        string gameName = string.IsNullOrWhiteSpace(ViewModel.GameProjectPath)
            ? "MeuJogo"
            : Path.GetFileNameWithoutExtension(ViewModel.GameProjectPath.TrimEnd('\\', '/'));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar Android — pasta do projeto Android",
            SuggestedFileName = $"{gameName}.Android",
        });

        if (file?.TryGetLocalPath() is not { } path)
            return;

        string parent = Path.GetDirectoryName(path)!;
        string folderName = Path.GetFileName(path);
        string androidProjectDir = Path.Combine(parent, folderName);

        string appId = "com.auroraengine." + System.Text.RegularExpressions.Regex
            .Replace(gameName.ToLowerInvariant(), "[^a-z0-9]", "");

        string? apk = await ViewModel.ExportAndroidAsync(androidProjectDir, appId, gameName);
        if (apk is not null)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{apk}\"")); }
            catch { /* abrir o Explorer é conveniência, não impede a exportação ter dado certo */ }
        }
    }

    private void OnExportAndroid(object? sender, RoutedEventArgs e) => _ = PickAndExportAndroidAsync();

    private void OnRefreshAssets(object? sender, RoutedEventArgs e)
    {
        ViewModel.ReloadAssets();
        Scene.ClearTextureCache();
    }

    private async Task PickAndImportAssetsAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar assets",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Imagem/Áudio/Fonte") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.wav", "*.ogg", "*.ttf"] },
                new FilePickerFileType("Todos os arquivos") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0)
            return;

        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Select(p => p!);
        try
        {
            ViewModel.ImportAssets(paths);
            Scene.ClearTextureCache();
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"Erro ao importar: {ex.Message}";
        }
    }

    private void OnImportAssets(object? sender, RoutedEventArgs e) => _ = PickAndImportAssetsAsync();

    /// <summary>
    /// Escolhe uma imagem para um campo de textura do inspector. Começa na pasta de assets do
    /// projeto; se o arquivo escolhido estiver fora dela, é copiado pra dentro — senão a cena
    /// guardaria um caminho que só existe nesta máquina e o jogo não acharia a textura.
    /// Devolve o caminho relativo pronto pra gravar na cena (null = cancelou ou deu erro).
    /// </summary>
    private async Task<string?> PickTextureAsync()
    {
        var startFolder = string.IsNullOrEmpty(ViewModel.AssetsRootDisplay)
            ? null
            : await StorageProvider.TryGetFolderFromPathAsync(ViewModel.AssetsRootDisplay);

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Escolher textura",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter =
            [
                new FilePickerFileType("Imagem") { Patterns = ["*.png", "*.jpg", "*.jpeg"] },
                new FilePickerFileType("Todos os arquivos") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
            return null;

        try
        {
            string? relative = ViewModel.EnsureAssetInProject(path);
            Scene.ClearTextureCache();       // arquivo novo (ou substituído) precisa ser relido
            return relative;
        }
        catch (Exception ex)
        {
            ViewModel.Status = $"Erro ao usar a imagem: {ex.Message}";
            return null;
        }
    }

    private void OnAssetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ViewModels.AssetViewModel asset)
            ViewModel.ApplyTextureToSelection(asset);
    }

    private void OnRefreshScenes(object? sender, RoutedEventArgs e) => ViewModel.ReloadSceneFiles();

    // ---------- Exclusão de arquivos do projeto ----------

    /// <summary>
    /// Confirma e apaga um arquivo do projeto. Um caminho só pros cinco painéis: o texto do
    /// diálogo muda, a regra (confirmar → apagar → recarregar a lista → avisar no status) não.
    /// </summary>
    private async Task ConfirmAndDeleteAsync(string? fullPath, string kind, Action reload)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            ViewModel.Status = $"Selecione {kind} na lista pra excluir.";
            return;
        }

        bool confirmed = await new ConfirmWindow(
            $"Excluir {kind} \"{System.IO.Path.GetFileName(fullPath)}\"?",
            $"O arquivo é apagado do disco. Não tem desfazer — o Ctrl+Z do editor volta edição " +
            $"de cena, não arquivo.\n\n{fullPath}")
            .ShowDialog<bool>(this);

        if (confirmed)
            ViewModel.Status = ViewModel.DeleteProjectFile(fullPath, reload);
    }

    private void OnDeleteScene(object? sender, RoutedEventArgs e)
    {
        // Apagar a cena aberta deixaria o editor mostrando um documento sem arquivo, e o próximo
        // Salvar recriaria o que o usuário acabou de mandar apagar.
        if (ViewModel.SelectedSceneFile is { IsCurrent: true })
        {
            ViewModel.Status = "Essa é a cena aberta — abra outra antes de excluir.";
            return;
        }

        _ = ConfirmAndDeleteAsync(ViewModel.SelectedSceneFile?.FullPath, "a cena",
            ViewModel.ReloadSceneFiles);
    }

    private void OnDeleteUiScreen(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedUiScreen is { IsCurrent: true })
        {
            ViewModel.Status = "Essa é a tela aberta — abra outra antes de excluir.";
            return;
        }

        _ = ConfirmAndDeleteAsync(ViewModel.SelectedUiScreen?.FullPath, "a tela de UI",
            ViewModel.ReloadUiScreens);
    }

    private void OnDeletePrefab(object? sender, RoutedEventArgs e)
        => _ = ConfirmAndDeleteAsync(ViewModel.SelectedPrefab?.FullPath, "o prefab",
            ViewModel.ReloadPrefabs);

    private void OnDeleteScript(object? sender, RoutedEventArgs e)
        => _ = ConfirmAndDeleteAsync(ViewModel.SelectedScript?.FullPath, "o script",
            ViewModel.ReloadScripts);

    private void OnDeleteAsset(object? sender, RoutedEventArgs e)
        => _ = ConfirmAndDeleteAsync(ViewModel.SelectedAsset?.FullPath, "o asset", () =>
        {
            ViewModel.ReloadAssets();
            // O canvas guarda a textura em cache por caminho: sem limpar, o sprite apagado
            // continuaria desenhado até reabrir o editor.
            Scene.ClearTextureCache();
        });

    private void OnRefreshUiScreens(object? sender, RoutedEventArgs e) => ViewModel.ReloadUiScreens();

    private void OnNewUiScreen(object? sender, RoutedEventArgs e) => _ = PickAndNewUiScreenAsync();

    private void OnSceneFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ViewModels.SceneFileViewModel file)
            ViewModel.OpenSceneFile(file);
    }

    private void OnRefreshPrefabs(object? sender, RoutedEventArgs e) => ViewModel.ReloadPrefabs();

    private void OnRefreshScripts(object? sender, RoutedEventArgs e) => ViewModel.RefreshScriptCatalog();

    private void OnRefreshScriptFiles(object? sender, RoutedEventArgs e) => ViewModel.ReloadScripts();

    /// <summary>"+ Novo…" do painel SCRIPTS: pergunta o template numa janelinha e abre o editor de
    /// código interno já com ele. O arquivo nasce no Salvar da própria janela
    /// (Scripts/&lt;Classe&gt;.cs), que também registra o [SceneScript] na hora — sem dialog de
    /// arquivo, sem editor externo e sem esperar build pra poder anexar numa entidade.</summary>
    private async Task PickTemplateAndOpenEditorAsync()
    {
        if (ViewModel.Document is null)
            return;

        bool create = await new ScriptTemplatePickerWindow(ViewModel).ShowDialog<bool>(this);
        if (!create)
            return;

        string scriptsDir = ViewModel.ScriptsDirPath;
        if (scriptsDir.Length > 0)
            Directory.CreateDirectory(scriptsDir);

        OpenScriptEditor(null);
    }

    private void OnNewScript(object? sender, RoutedEventArgs e) => _ = PickTemplateAndOpenEditorAsync();

    /// <summary>"Carregar…": abre um .cs existente no editor interno (começa na pasta Scripts do
    /// projeto, mas aceita qualquer caminho — abrir de fora serve pra copiar/colar código pronto
    /// e salvar como script novo).</summary>
    private async Task PickAndLoadScriptAsync()
    {
        string scriptsDir = ViewModel.ScriptsDirPath;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Carregar Script",
            AllowMultiple = false,
            SuggestedStartLocation = scriptsDir.Length > 0
                ? await StorageProvider.TryGetFolderFromPathAsync(scriptsDir)
                : null,
            FileTypeFilter =
            [
                new FilePickerFileType("Script C#") { Patterns = ["*.cs"] },
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            OpenScriptEditor(path);
    }

    private void OnLoadScript(object? sender, RoutedEventArgs e) => _ = PickAndLoadScriptAsync();

    /// <summary>Duplo-clique abre no editor interno; com Shift, no VS Code (quem já tem o VS Code
    /// configurado com analisadores não perde o caminho antigo).</summary>
    private void OnScriptFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not ViewModels.ScriptFileViewModel script)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            OpenInVsCode(script.FullPath);
        else
            OpenScriptEditor(script.FullPath);
    }

    /// <summary>Uma janela de editor por arquivo: reabrir o mesmo script traz a janela existente
    /// pra frente em vez de criar uma segunda cópia do mesmo texto (duas janelas salvando o mesmo
    /// arquivo perderiam edição).</summary>
    private void OpenScriptEditor(string? path)
    {
        // Compara pelo arquivo que cada janela está editando (não pela chave): um script criado
        // como "novo" vira um caminho depois do primeiro Salvar, e abrir ele pela lista precisa
        // cair na mesma janela.
        if (path is not null)
        {
            var open = _scriptEditors.Values.FirstOrDefault(
                w => string.Equals(w.CurrentPath, path, StringComparison.OrdinalIgnoreCase));
            if (open is not null)
            {
                open.Activate();
                return;
            }
        }

        string key = path ?? $"novo:{Guid.NewGuid():N}";

        var window = new ScriptEditorWindow(ViewModel, path);
        _scriptEditors[key] = window;
        window.Closed += (_, _) => _scriptEditors.Remove(key);
        window.Show(this);
    }

    private readonly Dictionary<string, ScriptEditorWindow> _scriptEditors = [];

    /// <summary>Abre um arquivo no VS Code via "code" no PATH (roda por cmd.exe pra resolver o
    /// shim .cmd que o instalador do VS Code registra — Process.Start direto com
    /// UseShellExecute=false não resolve extensão de batch). Se "code" não estiver instalado/no
    /// PATH, cai pro Explorer selecionando o arquivo, pra sempre sobrar algum jeito de abrir.</summary>
    private static void OpenInVsCode(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c code \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")); }
            catch { /* melhor esforço — sem VS Code no PATH nem Explorer, nada mais a fazer */ }
        }
    }

    private void OnPrefabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ViewModels.PrefabFileViewModel prefab)
        {
            var center = Scene.CameraCenter;
            ViewModel.CreatePrefabInstance(prefab, center.X, center.Y);
        }
    }

    private async Task PickSaveAsPrefabAsync()
    {
        if (ViewModel.SelectedEntity is not { } entity || ViewModel.Document is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar como prefab",
            DefaultExtension = "json",
            SuggestedFileName = $"{entity.Name}.json",
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                Path.Combine(ViewModel.Document.AssetsRoot, "prefabs")),
            FileTypeChoices =
            [
                new FilePickerFileType("Prefab Aurora (JSON)") { Patterns = ["*.json"] },
            ],
        });

        if (file?.TryGetLocalPath() is not { } path)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        entity.SaveAsPrefab(path);
        ViewModel.ReloadPrefabs();
        ViewModel.Status = $"Prefab salva: {Path.GetFileName(path)}";
    }

    private void OnSaveAsPrefab(object? sender, RoutedEventArgs e) => _ = PickSaveAsPrefabAsync();

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
