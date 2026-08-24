using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class EventTriggerViewModel : ComponentViewModel
{
    private readonly JsonObject _triggerNode;
    private readonly MainViewModel? _owner;

    public string[] TriggerTypes { get; } =
        ["SceneStart", "PlayerTouch", "PlayerInteract", "Touch", "Death", "SwitchOn", "KeyPress", "Timer",
         "VariableCompare", "HasItem", "QuestStageAtLeast"];

    public string[] CompareOps { get; } = [">=", "<=", ">", "<", "==", "!="];

    /// <summary>Teclas comuns pro gatilho KeyPress — sugestão, não lista fechada: cobre o que a
    /// maioria dos jogos liga sem impedir uma tecla exótica de ser digitada.</summary>
    public IEnumerable<string> KeyNames => MainViewModel.KeyNames;

    public ICommand AddActionCommand { get; }

    public ObservableCollection<EventActionViewModel> Actions { get; } = [];

    public EventTriggerViewModel(JsonObject node, MainViewModel? owner = null) : base(node, owner)
    {
        _triggerNode = node;
        _owner = owner;
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
            Raise(nameof(ShowKey));
            Raise(nameof(ShowInterval));
            Raise(nameof(ShowCompare));
            Raise(nameof(CompareLabel));
            Raise(nameof(TriggerDescription));
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

    public bool ShowRadius   => TriggerType is "PlayerTouch" or "PlayerInteract";
    public bool ShowTargetPrefix => TriggerType == "Touch";
    public bool ShowSwitch   => TriggerType == "SwitchOn";
    public bool ShowKey      => TriggerType is "KeyPress" or "PlayerInteract";
    public bool ShowInterval => TriggerType == "Timer";
    public bool ShowCompare  => TriggerType is "VariableCompare" or "HasItem" or "QuestStageAtLeast";

    /// <summary>Rótulo do campo "Variável" — muda pra Item/Quest quando o gatilho é sobre inventário/progresso.</summary>
    public string CompareLabel => TriggerType switch
    {
        "HasItem"          => "Item",
        "QuestStageAtLeast" => "Quest",
        _                  => "Variável",
    };

    public string TriggerDescription => TriggerType switch
    {
        "SceneStart"        => "Disparado ao carregar a cena",
        "PlayerTouch"       => "Disparado quando o jogador entra no raio",
        "PlayerInteract"    => "Disparado quando o jogador aperta o controle escolhido ESTANDO dentro do raio — é o \"encoste e aperte E\" de porta, NPC e alavanca",
        "Touch"             => "Disparado pela sobreposição dos colliders (usa a forma, não a distância)",
        "Death"             => "Disparado quando esta entidade morre — Wait/ShowChoice não suspendem aqui",
        "SwitchOn"          => "Disparado quando um switch é ativado",
        "KeyPress"          => "Disparado ao pressionar o controle em qualquer lugar do mapa (sem exigir proximidade)",
        "Timer"             => "Disparado em intervalos regulares",
        "VariableCompare"   => "Disparado quando variável atinge o valor",
        "HasItem"           => "Disparado quando a quantidade do item atinge o valor",
        "QuestStageAtLeast" => "Disparado quando a quest atinge o estágio",
        _                   => "",
    };

    public string Key
    {
        get => _triggerNode["Key"]?.GetValue<string>() ?? "E";
        set { _triggerNode["Key"] = value; Raise(); RaiseEdited("key"); }
    }

    /// <summary>Filtro do gatilho Touch: só entidades cujo nome comece com isto disparam.
    /// Vazio = qualquer uma com Collider.</summary>
    public string TargetPrefix
    {
        get => _triggerNode["TargetPrefix"]?.GetValue<string>() ?? "";
        set { _triggerNode["TargetPrefix"] = value; Raise(); RaiseEdited("targetPrefix"); }
    }

    public float Interval
    {
        get => _triggerNode["Interval"]?.GetValue<float>() ?? 5f;
        set { _triggerNode["Interval"] = value; Raise(); Raise(nameof(IntervalText)); RaiseEdited("interval"); }
    }

    public string IntervalText
    {
        get => Interval.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
                Interval = f;
        }
    }

    public string Variable
    {
        get => _triggerNode["Variable"]?.GetValue<string>() ?? "";
        set
        {
            if (string.IsNullOrEmpty(value)) _triggerNode.Remove("Variable");
            else _triggerNode["Variable"] = value;
            Raise();
            RaiseEdited("variable");
        }
    }

    public string CompareOp
    {
        get => _triggerNode["CompareOp"]?.GetValue<string>() ?? ">=";
        set { _triggerNode["CompareOp"] = value; Raise(); RaiseEdited("compare-op"); }
    }

    public float CompareValue
    {
        get => _triggerNode["CompareValue"]?.GetValue<float>() ?? 0f;
        set { _triggerNode["CompareValue"] = value; Raise(); Raise(nameof(CompareValueText)); RaiseEdited("compare-value"); }
    }

    public string CompareValueText
    {
        get => CompareValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
                CompareValue = f;
        }
    }

    public void AddAction()
    {
        var actionNode = new JsonObject { ["Action"] = "Wait", ["Seconds"] = 1f };
        if (_triggerNode["Actions"] is not JsonArray arr)
            _triggerNode["Actions"] = arr = [];
        arr.Add(actionNode);
        Actions.Add(new EventActionViewModel(actionNode, OnActionEdited, RemoveAction, _owner));
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
                Actions.Add(new EventActionViewModel(item, OnActionEdited, RemoveAction, _owner));
        }
    }
}
