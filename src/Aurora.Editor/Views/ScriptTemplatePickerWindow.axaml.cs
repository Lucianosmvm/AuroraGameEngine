using Aurora.Editor.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aurora.Editor.Views;

/// <summary>
/// Seletor de template do "+ Novo…" do painel SCRIPTS. Era um ComboBox fixo de 150px no cabeçalho
/// do painel, que estourava por cima dos botões quando a coluna esquerda era estreitada — e ficava
/// ocupando espaço permanente por uma escolha que só importa no instante de criar o script.
/// </summary>
public partial class ScriptTemplatePickerWindow : Window
{
    public ScriptTemplatePickerWindow()
        : this(new MainViewModel())
    {
    }

    public ScriptTemplatePickerWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        TemplateList.Focus();
    }

    private void OnCreate(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnTemplateDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is Models.ScriptTemplates.Template)
            Close(true);
    }
}
