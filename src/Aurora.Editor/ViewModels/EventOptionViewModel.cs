using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class EventOptionViewModel : ViewModelBase
{
    private readonly JsonObject _node;
    private readonly Action _onEdited;

    public ICommand RemoveCommand { get; }

    public EventOptionViewModel(JsonObject node, Action onEdited, Action<EventOptionViewModel> onRemove)
    {
        _node = node;
        _onEdited = onEdited;
        RemoveCommand = new RelayCommand(() => onRemove(this));
    }

    public string Text
    {
        get => _node["Text"]?.GetValue<string>() ?? "";
        set
        {
            _node["Text"] = value;
            Raise();
            _onEdited();
        }
    }

    public string SwitchName
    {
        get => _node["Switch"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value))
                _node.Remove("Switch");
            else
                _node["Switch"] = value;
            Raise();
            _onEdited();
        }
    }
}
