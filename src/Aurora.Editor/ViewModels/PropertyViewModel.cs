using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Aurora.Editor.Models;
using Avalonia.Media;
using Avalonia.Media.Imaging;

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

/// <summary>
/// Propriedade de textura (Texture, HoverTexture, PressedTexture…): mostra a imagem atual e
/// abre a lista de assets do projeto para escolher, com um atalho para procurar um arquivo no
/// computador — que é copiado para a pasta de assets na hora.
///
/// <para>O valor continua sendo o caminho relativo à raiz de assets ("sprites/player.png"),
/// que é o que o runtime carrega. Digitar à mão continua funcionando; o campo de texto está
/// lá do lado.</para>
/// </summary>
public sealed class TexturePropertyViewModel : PropertyViewModel
{
    private readonly MainViewModel? _owner;

    public TexturePropertyViewModel(JsonObject component, string name, MainViewModel? owner)
        : base(component, name)
    {
        _owner = owner;
        PickCommand = new RelayCommand(parameter =>
        {
            if (parameter is AssetViewModel asset)
                Value = asset.RelativePath;
        });
        ClearCommand = new RelayCommand(() => Value = "");
        BrowseCommand = new RelayCommand(() => _ = BrowseAsync());
    }

    public string Value
    {
        get => Component[Name]?.GetValue<string>() ?? "";
        set
        {
            if (Value == value)
                return;

            if (string.IsNullOrEmpty(value))
                Component.Remove(Name);
            else
                Component[Name] = value;

            Raise(nameof(Value));
            Raise(nameof(Preview));
            Raise(nameof(HasPreview));
            Raise(nameof(IsMissing));
            NotifyEdited();
        }
    }

    /// <summary>Texturas já dentro do projeto (painel ASSETS) — a lista do seletor.</summary>
    public IEnumerable<AssetViewModel> Assets => _owner?.Assets ?? [];

    /// <summary>Recebe o <see cref="AssetViewModel"/> clicado na lista.</summary>
    public ICommand PickCommand { get; }

    public ICommand ClearCommand { get; }

    /// <summary>Abre o seletor de arquivo do sistema (a janela é quem tem o StorageProvider).</summary>
    public ICommand BrowseCommand { get; }

    public Bitmap? Preview => Find(Value)?.Thumbnail;

    public bool HasPreview => Preview is not null;

    /// <summary>Campo preenchido com um caminho que não existe na pasta de assets — quase
    /// sempre erro de digitação, e no jogo vira exceção de textura não encontrada.</summary>
    public bool IsMissing => !string.IsNullOrEmpty(Value) && Find(Value) is null;

    private AssetViewModel? Find(string path)
        => string.IsNullOrEmpty(path)
            ? null
            : _owner?.Assets.FirstOrDefault(a => string.Equals(a.RelativePath, path, StringComparison.OrdinalIgnoreCase));

    private async Task BrowseAsync()
    {
        if (_owner?.PickTextureFromDisk is not { } pick)
            return;

        string? relative = await pick();
        if (!string.IsNullOrEmpty(relative))
            Value = relative;
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

/// <summary>Propriedade string com valores fixos (ex.: AnchorX/Y) — ComboBox em vez de TextBox.</summary>
/// <summary>
/// Uma opção de campo fechado: o que o autor lê e o que vai pro arquivo. Os dois são separados
/// porque o formato de cena é em inglês (Rain, Storm) e quem monta a cena não deveria precisar
/// decorar isso — a lista mostra "Chuva" e grava "Rain".
/// </summary>
public sealed record EnumOption(string Label, string Value)
{
    /// <summary>O ComboBox do Avalonia usa ToString quando não há template — é o que faz o
    /// rótulo aparecer sem precisar de um DataTemplate só pra isto.</summary>
    public override string ToString() => Label;
}

public sealed class EnumPropertyViewModel : PropertyViewModel
{
    private readonly string _fallback;

    public EnumOption[] Options { get; }

    public EnumPropertyViewModel(JsonObject component, string name, string fallback, EnumOption[] options)
        : base(component, name)
    {
        _fallback = fallback;
        Options = options;
    }

    /// <summary>
    /// Opção correspondente ao que está gravado. Null quando o arquivo tem um valor que não está
    /// na lista (JSON editado à mão, campo renomeado numa versão nova) — a caixa aparece VAZIA de
    /// propósito. Cair na primeira opção esconderia o problema e, no primeiro clique em qualquer
    /// outro campo, gravaria por cima do que o autor tinha escrito.
    /// </summary>
    public EnumOption? Selected
    {
        get => Array.Find(Options, o => o.Value == Value);
        set
        {
            if (value is not null)
                Value = value.Value;
        }
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
            Raise(nameof(Selected));
            NotifyEdited();
        }
    }
}

/// <summary>
/// Campo de texto com sugestões: aceita qualquer valor, mas oferece a lista do que existe no
/// projeto. É o meio-termo certo pros campos que apontam pra algo SEU — nome de entidade,
/// arquivo de prefab, id de tela — onde uma lista fechada seria errada (o alvo pode ainda não
/// existir, ou ser criado por script em jogo) e texto puro é um convite a errar a digitação e
/// passar meia hora procurando por que o inimigo não persegue ninguém.
/// </summary>
public sealed class SuggestPropertyViewModel : PropertyViewModel
{
    private readonly string _fallback;
    private readonly Func<IEnumerable<string>> _suggestions;

    /// <summary>Avaliado a cada leitura, não guardado: entidades e arquivos aparecem e somem
    /// enquanto o inspector está aberto, e uma lista congelada envelheceria na tela.</summary>
    public IEnumerable<string> Suggestions => _suggestions();

    /// <summary>Dica curta abaixo do campo — o que aquilo espera receber.</summary>
    public string Hint { get; }

    public SuggestPropertyViewModel(JsonObject component, string name, string fallback,
        Func<IEnumerable<string>> suggestions, string hint = "")
        : base(component, name)
    {
        _fallback = fallback;
        _suggestions = suggestions;
        Hint = hint;
    }

    public bool HasHint => Hint.Length > 0;

    public string Value
    {
        get => Component[Name]?.GetValue<string>() ?? _fallback;
        set
        {
            string incoming = value ?? "";
            if (Value == incoming)
                return;

            if (incoming.Length == 0) Component.Remove(Name);
            else Component[Name] = incoming;

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
