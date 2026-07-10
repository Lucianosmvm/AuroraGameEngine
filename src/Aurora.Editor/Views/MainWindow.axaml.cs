using Aurora.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Aurora.Editor.Views;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();

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
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O)
            {
                _ = PickAndOpenSceneAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && e.Source is not TextBox)
            {
                // Delete só fora de campos de texto — senão apagar caractere apaga entidade.
                ViewModel.DeleteSelectedEntity();
                e.Handled = true;
            }
        };
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

    private void OnOpenScene(object? sender, RoutedEventArgs e) => _ = PickAndOpenSceneAsync();

    private void OnSaveScene(object? sender, RoutedEventArgs e) => ViewModel.SaveScene();

    private void OnSaveSceneAs(object? sender, RoutedEventArgs e) => _ = PickAndSaveSceneAsAsync();

    private void OnCreateEntity(object? sender, RoutedEventArgs e)
    {
        var center = Scene.CameraCenter;
        ViewModel.CreateEntity(center.X, center.Y);
    }

    private void OnDeleteEntity(object? sender, RoutedEventArgs e) => ViewModel.DeleteSelectedEntity();

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
