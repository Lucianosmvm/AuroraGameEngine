using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class EventTriggerViewModel : ComponentViewModel
{
    private readonly JsonObject _triggerNode;

    public string[] TriggerTypes { get; } = ["SceneStart", "PlayerTouch", "SwitchOn"];

    public ICommand AddActionCommand { get; }

    public ObservableCollection<EventActionViewModel> Actions { get; } = [];

    public EventTriggerViewModel(JsonObject node) : base(node)
    {
        _triggerNode = node;
        AddActionCommand = new RelayCommand(AddAction);
        RebuildActions();
    }

    public string TriggerType
    {
        get => _triggerNode["Trigger"]?.GetValue<string>() ?? "PlayerTouch";
        set
        {
            _triggerNode["Trigger"] = value;
            Raise();
            Raise(nameof(ShowRadius));
            Raise(nameof(ShowSwitch));
            RaiseEdited("trigger");
        }
    }

    public bool Once
    {
        get => _triggerNode["Once"]?.GetValue<bool>() ?? true;
        set { _triggerNode["Once"] = value; Raise(); RaiseEdited("once"); }
    }

    public float Radius
    {
        get => _triggerNode["Radius"]?.GetValue<float>() ?? 20f;
        set { _triggerNode["Radius"] = value; Raise(); Raise(nameof(RadiusText)); RaiseEdited("radius"); }
    }

    public string RadiusText
    {
        get => Radius.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                Radius = f;
        }
    }

    public string SwitchName
    {
        get => _triggerNode["Switch"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) _triggerNode.Remove("Switch");
            else _triggerNode["Switch"] = value;
            Raise();
            RaiseEdited("switch");
        }
    }

    public bool ShowRadius => TriggerType == "PlayerTouch";
    public bool ShowSwitch => TriggerType == "SwitchOn";

    public void AddAction()
    {
        var actionNode = new JsonObject { ["Action"] = "Wait", ["Seconds"] = 1f };
        if (_triggerNode["Actions"] is not JsonArray arr)
            _triggerNode["Actions"] = arr = [];
        arr.Add(actionNode);
        Actions.Add(new EventActionViewModel(actionNode, OnActionEdited, RemoveAction));
        RaiseEdited("add-action");
    }

    public void RemoveAction(EventActionViewModel action)
    {
        int index = Actions.IndexOf(action);
        if (index >= 0 && _triggerNode["Actions"] is JsonArray arr && index < arr.Count)
            arr.RemoveAt(index);
        Actions.Remove(action);
        RaiseEdited("remove-action");
    }

    private void OnActionEdited() => RaiseEdited("action");

    private void RebuildActions()
    {
        Actions.Clear();
        if (_triggerNode["Actions"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
                Actions.Add(new EventActionViewModel(item, OnActionEdited, RemoveAction));
        }
    }
}
