using System.Globalization;
using System.Text.Json.Nodes;

namespace Aurora.Editor.ViewModels;

/// <summary>
/// Uma propriedade editável de um componente, espelhada no nó JSON.
/// Subclasses definem o editor usado no inspector (número, texto, bool).
/// </summary>
public abstract class PropertyViewModel : ViewModelBase
{
    protected readonly JsonObject Component;

    public string Name { get; }

    /// <summary>Disparado após qualquer edição — MainViewModel usa para marcar sujo e redesenhar.</summary>
    public event Action? Edited;

    protected PropertyViewModel(JsonObject component, string name)
    {
        Component = component;
        Name = name;
    }

    protected void NotifyEdited() => Edited?.Invoke();
}

public sealed class NumberPropertyViewModel : PropertyViewModel
{
    private readonly float _fallback;

    public NumberPropertyViewModel(JsonObject component, string name, float fallback)
        : base(component, name)
    {
        _fallback = fallback;
    }

    public float Value
    {
        get => Component[Name]?.GetValue<float>() ?? _fallback;
        set
        {
            if (Math.Abs(Value - value) < float.Epsilon)
                return;
            Component[Name] = value;
            Raise();
            Raise(nameof(Text));
            NotifyEdited();
        }
    }

    /// <summary>Ponte para TextBox: aceita vírgula ou ponto decimal.</summary>
    public string Text
    {
        get => Value.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                Value = parsed;
        }
    }

    /// <summary>Atualização externa (arrasto no canvas) — sincroniza o inspector.</summary>
    public void RefreshFromNode()
    {
        Raise(nameof(Value));
        Raise(nameof(Text));
    }
}

public sealed class TextPropertyViewModel : PropertyViewModel
{
    private readonly string _fallback;

    public TextPropertyViewModel(JsonObject component, string name, string fallback = "")
        : base(component, name)
    {
        _fallback = fallback;
    }

    public string Value
    {
        get => Component[Name]?.GetValue<string>() ?? _fallback;
        set
        {
            if (Value == value)
                return;
            if (string.IsNullOrEmpty(value) && string.IsNullOrEmpty(_fallback))
                Component.Remove(Name);
            else
                Component[Name] = value;
            Raise();
            NotifyEdited();
        }
    }
}

public sealed class BoolPropertyViewModel : PropertyViewModel
{
    private readonly bool _fallback;

    public BoolPropertyViewModel(JsonObject component, string name, bool fallback)
        : base(component, name)
    {
        _fallback = fallback;
    }

    public bool Value
    {
        get => Component[Name]?.GetValue<bool>() ?? _fallback;
        set
        {
            if (Value == value)
                return;
            Component[Name] = value;
            Raise();
            NotifyEdited();
        }
    }
}
