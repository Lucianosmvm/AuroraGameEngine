using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

/// <summary>Entidade da cena na hierarquia/inspector, espelhada no nó JSON.</summary>
public sealed class EntityViewModel : ViewModelBase
{
    public JsonObject Node { get; }
    public ObservableCollection<ComponentViewModel> Components { get; } = [];

    /// <summary>Tag identifica o gesto de edição (coalescência de undo).</summary>
    public event Action<string>? Edited;

    private static readonly string[] BuiltInComponentTypes =
        ["SpriteRenderer", "Animator", "Collider", "CameraController", "EventTrigger"];

    private readonly MainViewModel? _owner;

    /// <summary>Nativos + scripts [SceneScript] descobertos no projeto do jogo (ver MainViewModel.CustomScripts).</summary>
    public IEnumerable<string> AvailableComponentTypes =>
        BuiltInComponentTypes.Concat(_owner?.CustomScripts.Select(s => s.Name) ?? []);

    private string _newComponentType = "Collider";
    public string NewComponentType
    {
        get => _newComponentType;
        set => Set(ref _newComponentType, value);
    }

    public ICommand AddComponentCommand { get; }

    public EntityViewModel(JsonObject node, MainViewModel? owner = null)
    {
        Node = node;
        _owner = owner;
        AddComponentCommand = new RelayCommand(AddComponent);

        if (owner is not null)
            owner.CustomScripts.CollectionChanged += (_, _) => Raise(nameof(AvailableComponentTypes));

        if (node["Components"] is JsonArray components)
        {
            foreach (var componentNode in components.OfType<JsonObject>())
                AddVm(BuildVm(componentNode));
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
    public ComponentViewModel? Tilemap => Component("Tilemap");
    public ComponentViewModel? Camera => Component("CameraController");

    // ---- EventTrigger visibility in hierarchy ----

    public bool HasEventTrigger => Components.Any(c => c.Type == "EventTrigger");

    public string TriggerTypeLabel
    {
        get
        {
            var etvm = Components.OfType<EventTriggerViewModel>().FirstOrDefault();
            return etvm?.TriggerType ?? "";
        }
    }

    // ---- Add / Remove ----

    public void AddComponent()
    {
        if (Node["Components"] is not JsonArray components)
            return;

        JsonObject newNode = NewComponentType switch
        {
            "Animator" => new JsonObject
            {
                ["Type"] = "Animator",
                ["FrameWidth"] = 16,
                ["FrameHeight"] = 16,
                ["SheetColumns"] = 1,
                ["Clips"] = new JsonArray(),
            },
            "Collider" => new JsonObject
            {
                ["Type"] = "Collider",
                ["Width"] = 16f,
                ["Height"] = 16f,
            },
            "CameraController" => new JsonObject
            {
                ["Type"] = "CameraController",
                ["Zoom"] = 1f,
                ["ViewWidth"] = 1280,
                ["ViewHeight"] = 720,
            },
            "EventTrigger" => new JsonObject
            {
                ["Type"] = "EventTrigger",
                ["Trigger"] = "PlayerTouch",
                ["Once"] = true,
                ["Actions"] = new JsonArray(),
            },
            "SpriteRenderer" => new JsonObject
            {
                ["Type"] = "SpriteRenderer",
            },
            _ => BuildCustomScriptNode(),
        };

        components.Add(newNode);
        AddVm(BuildVm(newNode));
        Edited?.Invoke($"addcomp:{Node.GetHashCode()}");
    }

    /// <summary>
    /// NewComponentType não bateu com nenhum nativo — procura nos scripts [SceneScript]
    /// descobertos e pré-popula um campo por propriedade pública (com default), pro
    /// ComponentViewModel genérico já renderizar editor pra cada um. Nome desconhecido
    /// (script ainda não descoberto/buildado) vira só "Type" mesmo, sem campos.
    /// </summary>
    private JsonObject BuildCustomScriptNode()
    {
        var node = new JsonObject { ["Type"] = NewComponentType };

        var script = _owner?.CustomScripts.FirstOrDefault(s => s.Name == NewComponentType);
        if (script is null)
            return node;

        foreach (var field in script.Fields)
        {
            node[field.Name] = field.Kind switch
            {
                "float" => float.TryParse(field.Default,
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out float f) ? f : 0f,
                "int" => int.TryParse(field.Default, out int i) ? i : 0,
                "bool" => field.Default == "true",
                _ => field.Default,
            };
        }
        return node;
    }

    public void RemoveComponent(ComponentViewModel vm)
    {
        if (Node["Components"] is JsonArray components)
        {
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] is JsonObject obj && ReferenceEquals(obj, vm.Node))
                {
                    components.RemoveAt(i);
                    break;
                }
            }
        }
        Components.Remove(vm);
        Raise(nameof(HasEventTrigger));
        Raise(nameof(TriggerTypeLabel));
        Edited?.Invoke($"removecomp:{Node.GetHashCode()}");
    }

    // ---- Tile painting ----

    /// <summary>Pinta uma célula do tilemap. Um traço contínuo = um passo de undo.</summary>
    public void SetTile(int x, int y, int index)
    {
        var map = Tilemap;
        if (map is null)
            return;

        int width = (int)map.GetFloat("Width", 0f);
        int height = (int)map.GetFloat("Height", 0f);
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;

        if (map.Node["Tiles"] is not JsonArray tiles)
            map.Node["Tiles"] = tiles = [];

        while (tiles.Count < width * height)
            tiles.Add(-1);

        int cell = y * width + x;
        if (tiles[cell]?.GetValue<int>() == index)
            return;

        tiles[cell] = index;
        Edited?.Invoke($"paint:{Node.GetHashCode()}");
    }

    // ---- Transform helpers ----

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

    // ---- Helpers ----

    private static ComponentViewModel BuildVm(JsonObject node) =>
        node["Type"]?.GetValue<string>() switch
        {
            "EventTrigger" => new EventTriggerViewModel(node),
            "Animator"     => new AnimatorViewModel(node),
            _              => new ComponentViewModel(node),
        };

    private void AddVm(ComponentViewModel vm)
    {
        if (vm.Type != "Transform")
            vm.RemoveCommand = new RelayCommand(() => RemoveComponent(vm));

        vm.Edited += tag => Edited?.Invoke($"{Node.GetHashCode()}/{tag}");

        if (vm is EventTriggerViewModel etvm)
        {
            etvm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(EventTriggerViewModel.TriggerType))
                    Raise(nameof(TriggerTypeLabel));
            };
        }

        Components.Add(vm);
        Raise(nameof(HasEventTrigger));
        Raise(nameof(TriggerTypeLabel));
    }
}
