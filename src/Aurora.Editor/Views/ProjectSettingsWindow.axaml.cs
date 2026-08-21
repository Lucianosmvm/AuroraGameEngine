using Aurora.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Aurora.Editor.Views;

/// <summary>
/// Configurações do projeto (assets root, caminho do jogo, tela de referência e orientação
/// Android). Moraram no topo do Inspector até virarem ruído: são ajustes que se mexem uma vez
/// por projeto e roubavam metade do painel de quem edita entidade o tempo todo.
/// </summary>
public partial class ProjectSettingsWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext!;

    /// <summary>Disparado quando o assets root muda — o dono limpa o cache de texturas do canvas.</summary>
    public event System.Action? AssetsRootChanged;

    public ProjectSettingsWindow()
        : this(new MainViewModel())
    {
    }

    public ProjectSettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

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
            AssetsRootChanged?.Invoke();
        }
    }

    private void OnChangeAssetsRoot(object? sender, RoutedEventArgs e) => _ = PickAssetsRootAsync();

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

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
