using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;
using Avalonia.Media;

namespace Aurora.Editor.ViewModels;

/// <summary>
/// Uma propriedade editável de um componente, espelhada no nó JSON.
/// Subclasses definem o editor usado no inspector (número, texto, bool).
/// </summary>
public abstract class PropertyViewModel : ViewModelBase
{
    protected readonly JsonObject Component;

    public string Name { get; }

    /// <summary>
    /// Disparado após qualquer edição, com uma tag que identifica o gesto —
    /// edições consecutivas com a mesma tag colapsam num só passo de undo.
    /// </summary>
    public event Action<string>? Edited;

    protected PropertyViewModel(JsonObject component, string name)
    {
        Component = component;
        Name = name;
    }

    protected void NotifyEdited() => Edited?.Invoke(Name);

    /// <summary>
    /// Lê um número do nó JSON como float, tolerando inteiros armazenados como
    /// Int32/Int64 (System.Text.Json não converte implicitamente para Single).
    /// </summary>
    internal static float ReadFloat(JsonNode? node, float fallback)
    {
        if (node is not JsonValue jv) return fallback;
        if (jv.TryGetValue(out float  f)) return f;
        if (jv.TryGetValue(out double d)) return (float)d;
        if (jv.TryGetValue(out long   l)) return l;
        if (jv.TryGetValue(out int    i)) return i;
        return fallback;
    }
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
        get => ReadFloat(Component[Name], _fallback);
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

/// <summary>
/// Propriedade de cor — qualquer campo guardado como hex do engine ("#RRGGBBAA"). Mostra a
/// cor atual num quadradinho que abre a paleta nomeada, com a opacidade num controle
/// separado, e mantém o campo de texto para quem já sabe o hex.
///
/// <para>Existe porque o inspector tratava cor como texto livre: para deixar um botão azul
/// era preciso saber de cabeça (ou procurar na internet) o hexadecimal.</para>
/// </summary>
public sealed class ColorPropertyViewModel : PropertyViewModel
{
    private readonly string _fallback;

    public ColorPropertyViewModel(JsonObject component, string name, string fallback)
        : base(component, name)
    {
        _fallback = string.IsNullOrWhiteSpace(fallback) ? "#FFFFFFFF" : fallback;

        // Cada quadradinho já nasce com o comando desta propriedade. Custa 40 objetos por
        // campo de cor e evita o botão do template ter que procurar a propriedade subindo a
        // árvore visual de dentro do popup — que é onde binding de flyout costuma quebrar.
        Choices = [.. ColorPalette.Swatches.Select(swatch => new SwatchChoiceViewModel(swatch, () => Apply(swatch)))];
    }

    /// <summary>Hex cru, do jeito que vai para o JSON. Continua editável à mão.</summary>
    public string Value
    {
        get => Component[Name]?.GetValue<string>() ?? _fallback;
        set
        {
            if (Value == value)
                return;

            Component[Name] = value;
            RaiseAll();
            NotifyEdited();
        }
    }

    /// <summary>As cores da paleta, cada uma com o comando que a aplica neste campo.</summary>
    public IReadOnlyList<SwatchChoiceViewModel> Choices { get; }

    /// <summary>Cor atual já convertida (branco quando o texto não é hex válido).</summary>
    public Color Color => EngineColor.Parse(Value, Colors.White);

    public IBrush Preview => new SolidColorBrush(Color);

    /// <summary>Nome da cor quando ela está na paleta; senão, o próprio hex.</summary>
    public string ColorName => ColorPalette.NameOf(Color) ?? EngineColor.ToHex(Color);

    /// <summary>Opacidade em porcentagem — o canal alpha do hex em linguagem de gente.</summary>
    public double Opacity
    {
        get => Math.Round(Color.A / 255.0 * 100.0);
        set
        {
            byte alpha = (byte)Math.Clamp(Math.Round(value / 100.0 * 255.0), 0, 255);
            if (alpha == Color.A)
                return;

            Component[Name] = EngineColor.WithAlpha(Color, alpha);
            RaiseAll();
            NotifyEdited();
        }
    }

    public string OpacityText => $"{Opacity:0}%";

    /// <summary>Escolha na paleta: troca a cor e só mexe na opacidade se o swatch trouxer a dele.</summary>
    private void Apply(ColorSwatch swatch)
        => Value = EngineColor.WithAlpha(swatch.Color, swatch.CarriesAlpha ? swatch.Color.A : Color.A);

    private void RaiseAll()
    {
        Raise(nameof(Value));
        Raise(nameof(Color));
        Raise(nameof(Preview));
        Raise(nameof(ColorName));
        Raise(nameof(Opacity));
        Raise(nameof(OpacityText));
    }
}

/// <summary>Um quadradinho da paleta dentro de um campo de cor: o que desenhar, o texto da
/// dica (que também é o nome lido por leitor de tela) e o comando que aplica a cor.</summary>
public sealed class SwatchChoiceViewModel
{
    public SwatchChoiceViewModel(ColorSwatch swatch, Action apply)
    {
        Swatch = swatch;
        Command = new RelayCommand(apply);
    }

    public ColorSwatch Swatch { get; }
    public ICommand Command { get; }
    public IBrush Brush => Swatch.Brush;
    public string Label => Swatch.Label;
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

/// <summary>Propriedade string com valores fixos (ex.: AnchorX/Y) — ComboBox em vez de TextBox.</summary>
public sealed class EnumPropertyViewModel : PropertyViewModel
{
    private readonly string _fallback;

    public string[] Options { get; }

    public EnumPropertyViewModel(JsonObject component, string name, string fallback, string[] options)
        : base(component, name)
    {
        _fallback = fallback;
        Options = options;
    }

    public string Value
    {
        get => Component[Name]?.GetValue<string>() ?? _fallback;
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
