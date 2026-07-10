using System.Text.Json.Nodes;

namespace Aurora.Editor.ViewModels;

/// <summary>Entidade da cena na hierarquia/inspector, espelhada no nó JSON.</summary>
public sealed class EntityViewModel : ViewModelBase
{
    public JsonObject Node { get; }
    public List<ComponentViewModel> Components { get; } = [];

    public event Action? Edited;

    public EntityViewModel(JsonObject node)
    {
        Node = node;

        if (node["Components"] is JsonArray components)
        {
            foreach (var componentNode in components.OfType<JsonObject>())
            {
                var component = new ComponentViewModel(componentNode);
                component.Edited += () => Edited?.Invoke();
                Components.Add(component);
            }
        }
    }

    public string Name
    {
        get => Node["Name"]?.GetValue<string>() ?? "Entity";
        set
        {
            if (Name == value)
                return;
            Node["Name"] = value;
            Raise();
            Edited?.Invoke();
        }
    }

    public ComponentViewModel? Component(string type)
        => Components.FirstOrDefault(c => c.Type == type);

    public ComponentViewModel? Transform => Component("Transform");
    public ComponentViewModel? Sprite => Component("SpriteRenderer");

    /// <summary>Move a entidade (arrasto no canvas) e sincroniza o inspector.</summary>
    public void SetPosition(float x, float y)
    {
        var transform = Transform;
        if (transform is null)
            return;

        var px = transform.Number("X");
        var py = transform.Number("Y");
        if (px is null || py is null)
            return;

        px.Value = x;
        py.Value = y;
        px.RefreshFromNode();
        py.RefreshFromNode();
    }
}
