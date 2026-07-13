using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

public sealed class AnimatorTransitionViewModel : ViewModelBase
{
    private readonly JsonObject _node;
    private readonly Action _onEdited;

    public ICommand RemoveCommand { get; }

    public string[] CompareOps { get; } = [">=", "<=", ">", "<", "==", "!="];

    public AnimatorTransitionViewModel(JsonObject node, Action onEdited, Action<AnimatorTransitionViewModel> onRemove)
    {
        _node = node;
        _onEdited = onEdited;
        RemoveCommand = new RelayCommand(() => onRemove(this));
    }

    /// <summary>Nome do clipe de origem, ou "Any" pra casar com qualquer clipe atual.</summary>
    public string From
    {
        get => _node["From"]?.GetValue<string>() ?? "Any";
        set { _node["From"] = value; Raise(); _onEdited(); }
    }

    public string To
    {
        get => _node["To"]?.GetValue<string>() ?? "";
        set { _node["To"] = value; Raise(); _onEdited(); }
    }

    /// <summary>Nome do parâmetro local do Animator (SetFloat/SetBool no script).</summary>
    public string Parameter
    {
        get => _node["Parameter"]?.GetValue<string>() ?? "";
        set { _node["Parameter"] = value; Raise(); _onEdited(); }
    }

    public bool IsBool
    {
        get => _node["IsBool"]?.GetValue<bool>() ?? false;
        set { _node["IsBool"] = value; Raise(); Raise(nameof(ShowCompare)); Raise(nameof(ShowBoolValue)); _onEdited(); }
    }

    public bool ShowCompare => !IsBool;
    public bool ShowBoolValue => IsBool;

    public string CompareOp
    {
        get => _node["CompareOp"]?.GetValue<string>() ?? ">=";
        set { _node["CompareOp"] = value; Raise(); _onEdited(); }
    }

    public float CompareValue
    {
        get => _node["CompareValue"]?.GetValue<float>() ?? 0f;
        set { _node["CompareValue"] = value; Raise(); Raise(nameof(CompareValueText)); _onEdited(); }
    }

    public string CompareValueText
    {
        get => CompareValue.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                CompareValue = f;
        }
    }

    public bool BoolValue
    {
        get => _node["BoolValue"]?.GetValue<bool>() ?? true;
        set { _node["BoolValue"] = value; Raise(); _onEdited(); }
    }
}
