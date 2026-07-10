using System.Text.Json.Nodes;

namespace Aurora.Editor.ViewModels;

/// <summary>Entidade da cena na hierarquia/inspector, espelhada no nó JSON.</summary>
public sealed class EntityViewModel : ViewModelBase
{
    public JsonObject Node { get; }
    public List<ComponentViewModel> Components { get; } = [];

    /// <summary>Tag identifica o gesto de edição (coalescência de undo).</summary>
    public event Action<string>? Edited;

    public EntityViewModel(JsonObject node)
    {
        Node = node;

        if (node["Components"] is JsonArray components)
        {
            foreach (var componentNode in components.OfType<JsonObject>())
            {
                var component = new ComponentViewModel(componentNode);
                component.Edited += tag => Edited?.Invoke($"{Node.GetHashCode()}/{tag}");
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
            Edited?.Invoke($"rename:{Node.GetHashCode()}");
        }
    }

    public ComponentViewModel? Component(string type)
        => Components.FirstOrDefault(c => c.Type == type);

    public ComponentViewModel? Transform => Component("Transform");
    public ComponentViewModel? Sprite => Component("SpriteRenderer");

    /// <summary>Move a entidade (arrasto no canvas), sincronizando o inspector. Um gesto = um undo.</summary>
    public void SetPosition(float x, float y)
        => SetTransformFields($"move:{Node.GetHashCode()}", ("X", x), ("Y", y));

    public void SetScale(float scaleX, float scaleY)
        => SetTransformFields($"scale:{Node.GetHashCode()}", ("ScaleX", scaleX), ("ScaleY", scaleY));

    public void SetRotation(float radians)
        => SetTransformFields($"rotate:{Node.GetHashCode()}", ("Rotation", radians));

    private void SetTransformFields(string tag, params (string Name, float Value)[] fields)
    {
        var transform = Transform;
        if (transform is null)
            return;

        foreach (var (name, value) in fields)
        {
            transform.Node[name] = value;
            transform.Number(name)?.RefreshFromNode();
        }

        Edited?.Invoke(tag);
    }
}
