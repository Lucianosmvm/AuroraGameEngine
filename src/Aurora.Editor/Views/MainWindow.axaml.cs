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
            if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S)
            {
                ViewModel.SaveScene();
                e.Handled = true;
            }
            else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.O)
            {
                _ = PickAndOpenSceneAsync();
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

    private void OnOpenScene(object? sender, RoutedEventArgs e) => _ = PickAndOpenSceneAsync();

    private void OnSaveScene(object? sender, RoutedEventArgs e) => ViewModel.SaveScene();

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}
