using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

/// <summary>UiButton: campos escalares (X/Y/Width/Height/Text/cores, via ComponentViewModel
/// genérico) + lista de ações OnClick — mesma edição de EventTriggerViewModel.Actions.</summary>
public sealed class UiButtonViewModel : ComponentViewModel
{
    private readonly JsonObject _node;

    public ICommand AddActionCommand { get; }

    public ObservableCollection<EventActionViewModel> Actions { get; } = [];

    public UiButtonViewModel(JsonObject node) : base(node)
    {
        _node = node;
        AddActionCommand = new RelayCommand(AddAction);
        RebuildActions();
    }

    public void AddAction()
    {
        var actionNode = new JsonObject { ["Action"] = "Wait", ["Seconds"] = 1f };
        if (_node["OnClick"] is not JsonArray arr)
            _node["OnClick"] = arr = [];
        arr.Add(actionNode);
        Actions.Add(new EventActionViewModel(actionNode, OnActionEdited, RemoveAction));
        RaiseEdited("add-action");
    }

    public void RemoveAction(EventActionViewModel action)
    {
        int index = Actions.IndexOf(action);
        if (index >= 0 && _node["OnClick"] is JsonArray arr && index < arr.Count)
            arr.RemoveAt(index);
        Actions.Remove(action);
        RaiseEdited("remove-action");
    }

    private void OnActionEdited() => RaiseEdited("action");

    private void RebuildActions()
    {
        Actions.Clear();
        if (_node["OnClick"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
                Actions.Add(new EventActionViewModel(item, OnActionEdited, RemoveAction));
        }
    }
}
