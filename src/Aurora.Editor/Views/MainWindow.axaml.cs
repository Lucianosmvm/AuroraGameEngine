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

    private void OnRefreshAssets(object? sender, RoutedEventArgs e)
    {
        ViewModel.ReloadAssets();
        Scene.ClearTextureCache();
    }

    private void OnAssetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is ViewModels.AssetViewModel asset)
            ViewModel.ApplyTextureToSelection(asset);
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
