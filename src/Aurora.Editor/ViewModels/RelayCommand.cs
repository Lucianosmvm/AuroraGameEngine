using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public RelayCommand(Action execute) => _execute = execute;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
