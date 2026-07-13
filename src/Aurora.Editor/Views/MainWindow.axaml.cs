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

    private async Task PickAssetsRootAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecione a pasta raiz de assets",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            ViewModel.ChangeAssetsRoot(path);
            Scene.ClearTextureCache();
        }
    }

    private void OnChangeAssetsRoot(object? sender, RoutedEventArgs e) => _ = PickAssetsRootAsync();

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

    private async Task PickGameProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecione o .csproj ou executável do jogo",
            FileTypeFilter =
            [
                new FilePickerFileType("Projeto C# ou executável") { Patterns = ["*.csproj", "*.exe", "*.dll"] },
                new FilePickerFileType("Todos os arquivos") { Patterns = ["*"] },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            ViewModel.GameProjectPath = path;
    }

    private void OnBrowseGameProject(object? sender, RoutedEventArgs e) => _ = PickGameProjectAsync();

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

    private void OnAssetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ViewModels.AssetViewModel asset)
            ViewModel.ApplyTextureToSelection(asset);
    }

    private void OnRefreshScenes(object? sender, RoutedEventArgs e) => ViewModel.ReloadSceneFiles();

    private void OnRefreshUiScreens(object? sender, RoutedEventArgs e) => ViewModel.ReloadUiScreens();

    private void OnNewUiScreen(object? sender, RoutedEventArgs e) => _ = PickAndNewUiScreenAsync();

    private void OnSceneFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ViewModels.SceneFileViewModel file)
            ViewModel.OpenSceneFile(file);
    }

    private void OnRefreshPrefabs(object? sender, RoutedEventArgs e) => ViewModel.ReloadPrefabs();

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
