using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;

#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public RelayCommand(Action execute) => _execute = _ => execute();

    /// <summary>Versão que recebe o CommandParameter — para listas, onde o botão existe uma
    /// vez no template e o item clicado chega como parâmetro (ex.: a cor da paleta).</summary>
    public RelayCommand(Action<object?> execute) => _execute = execute;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute(parameter);
}
