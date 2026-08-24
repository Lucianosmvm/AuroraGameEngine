using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Aurora.Editor.Views;

/// <summary>
/// Diálogo de sim/não. Existe por causa da exclusão de arquivo: apagar cena, prefab, script ou
/// asset mexe no disco e não tem desfazer — o Ctrl+Z do editor só volta edição de cena, não
/// ressuscita arquivo. Um clique errado sem confirmação levaria trabalho embora.
/// </summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow()
        : this("Confirmar?", "", "Excluir")
    {
    }

    public ConfirmWindow(string message, string detail, string confirmLabel = "Excluir")
    {
        InitializeComponent();
        MessageText.Text = message;
        DetailText.Text = detail;
        DetailText.IsVisible = detail.Length > 0;
        ConfirmButton.Content = confirmLabel;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
