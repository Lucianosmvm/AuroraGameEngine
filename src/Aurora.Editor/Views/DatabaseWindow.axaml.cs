using Aurora.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aurora.Editor.Views;

/// <summary>
/// Banco de dados do projeto — o equivalente à aba do RPG Maker, com uma aba por catálogo:
/// <b>Itens</b> (<c>database/items.json</c>) e <b>Tabelas de Spawn</b>
/// (<c>database/spawns.json</c>). São os mesmos arquivos que o runtime carrega sozinho no boot,
/// então o que se cadastra aqui já vale no jogo sem nenhuma linha de código.
///
/// <para>Inimigo e objeto de cena continuam sendo prefab — lá o caminho do arquivo já faz o
/// papel de id. As tabelas de spawn não os duplicam: elas AGRUPAM prefabs sob um id, com peso e
/// condição, pra cena poder dizer "um inimigo da floresta" em vez de nomear um arquivo.</para>
/// </summary>
public partial class DatabaseWindow : Window
{
    private DatabaseViewModel ViewModel => (DatabaseViewModel)DataContext!;

    /// <summary>Construtor sem parâmetro pro designer do Avalonia.</summary>
    public DatabaseWindow()
        : this(new DatabaseViewModel(new MainViewModel()))
    {
    }

    public DatabaseWindow(DatabaseViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnSave(object? sender, RoutedEventArgs e) => ViewModel.Save();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
