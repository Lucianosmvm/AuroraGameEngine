using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aurora.Editor.ViewModels;

/// <summary>Componente de uma entidade no inspector.</summary>
public sealed class ComponentViewModel : ViewModelBase
{
    /// <summary>
    /// Campos canônicos dos componentes nativos: aparecem no inspector mesmo quando
    /// ausentes do JSON (o JSON omite valores default). Componentes fora desta lista
    /// mostram só o que o JSON tem — e são preservados no save do mesmo jeito.
    /// </summary>
    private static readonly Dictionary<string, (string Name, object Default)[]> KnownSchemas = new()
    {
        ["Transform"] =
        [
            ("X", 0f), ("Y", 0f), ("Rotation", 0f), ("ScaleX", 1f), ("ScaleY", 1f),
        ],
        ["SpriteRenderer"] =
        [
            ("Texture", ""), ("Layer", 0f), ("OriginX", 0.5f), ("OriginY", 0.5f),
            ("FlipX", false), ("FlipY", false), ("Visible", true), ("Color", "#FFFFFFFF"),
        ],
    };

    public JsonObject Node { get; }
    public string Type { get; }
    public List<PropertyViewModel> Properties { get; } = [];

    public event Action<string>? Edited;

    public ComponentViewModel(JsonObject node)
    {
        Node = node;
        Type = node["Type"]?.GetValue<string>() ?? "?";

        var added = new HashSet<string> { "Type" };

        if (KnownSchemas.TryGetValue(Type, out var schema))
        {
            foreach (var (name, fallback) in schema)
            {
                AddProperty(name, fallback);
                added.Add(name);
            }
        }

        // Campos presentes no JSON além do esquema (componentes de jogo/plugins).
        foreach (var (name, value) in Node)
        {
            if (added.Contains(name) || value is null)
                continue;

            object fallback = value.GetValueKind() switch
            {
                JsonValueKind.Number => 0f,
                JsonValueKind.True or JsonValueKind.False => false,
                _ => "",
            };
            AddProperty(name, fallback);
        }
    }

    private void AddProperty(string name, object fallback)
    {
        PropertyViewModel property = fallback switch
        {
            float number => new NumberPropertyViewModel(Node, name, number),
            bool flag => new BoolPropertyViewModel(Node, name, flag),
            _ => new TextPropertyViewModel(Node, name, (string)fallback),
        };
        property.Edited += tag => Edited?.Invoke($"{Type}.{tag}");
        Properties.Add(property);
    }

    public NumberPropertyViewModel? Number(string name)
        => Properties.OfType<NumberPropertyViewModel>().FirstOrDefault(p => p.Name == name);

    public TextPropertyViewModel? Text(string name)
        => Properties.OfType<TextPropertyViewModel>().FirstOrDefault(p => p.Name == name);

    // Leitura direta do nó para renderização do canvas (sem passar pelos VMs de propriedade).
    public float GetFloat(string name, float fallback) => Node[name]?.GetValue<float>() ?? fallback;
    public string? GetString(string name) => Node[name]?.GetValue<string>();
    public bool GetBool(string name, bool fallback) => Node[name]?.GetValue<bool>() ?? fallback;
}
