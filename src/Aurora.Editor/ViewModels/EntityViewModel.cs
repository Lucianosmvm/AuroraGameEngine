using System.Collections.ObjectModel;
using System.Text.Json;
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

    private static readonly string[] GameplayComponentTypes =
        ["SpriteRenderer", "Animator", "Collider", "Health", "Projectile", "CameraController",
         "EventTrigger", "ParticleEmitter", "Light2D", "GlobalTint", "NavAgent"];

    // UiText/UiImage/UiBar/UiPanel/UiButton só existem no sistema UIManager (telas de HUD/menu,
    // coordenadas de pixel de tela, sem câmera) — não são IComponent do SceneSerializer normal.
    // Numa entidade de cena comum eles travam o load do jogo (componente não registrado).
    private static readonly string[] UiComponentTypes =
        ["UiText", "UiImage", "UiBar", "UiPanel", "UiButton", "UiJoystick"];

    private readonly MainViewModel? _owner;

    /// <summary>Nativos (filtrados por tipo de documento — UI vs gameplay) + scripts
    /// [SceneScript] descobertos no projeto do jogo (ver MainViewModel.CustomScripts).</summary>
    public IEnumerable<string> AvailableComponentTypes =>
        (_owner?.IsUiScreenDocument == true ? UiComponentTypes : GameplayComponentTypes)
        .Concat(_owner?.CustomScripts.Select(s => s.Name) ?? []);

    private string _newComponentType = "Collider";
    public string NewComponentType
    {
        get => _newComponentType;
        set => Set(ref _newComponentType, value);
    }

    public ICommand AddComponentCommand { get; }
    public ICommand SyncFromPrefabCommand { get; }
    public ICommand ApplyToPrefabCommand { get; }

    public EntityViewModel(JsonObject node, MainViewModel? owner = null)
    {
        Node = node;
        _owner = owner;
        AddComponentCommand = new RelayCommand(AddComponent);
        SyncFromPrefabCommand = new RelayCommand(() => SyncFromPrefab());
        ApplyToPrefabCommand = new RelayCommand(() => ApplyToPrefab());

        if (owner is not null)
            owner.CustomScripts.CollectionChanged += (_, _) => Raise(nameof(AvailableComponentTypes));

        RebuildComponents();
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

    // ---- Prefabs ----

    /// <summary>Caminho relativo (à raiz de assets) da prefab linkada, se houver. Campo "Prefab"
    /// na cena, ignorado pelo runtime (não faz parte do schema que ele lê).</summary>
    public string? PrefabPath
    {
        get => Node["Prefab"]?.GetValue<string>();
        private set
        {
            if (value is null) Node.Remove("Prefab");
            else Node["Prefab"] = value;
            Raise();
            Raise(nameof(HasPrefab));
        }
    }

    public bool HasPrefab => PrefabPath is not null;

    /// <summary>Salva os Components desta entidade (exceto Transform — posição é por instância,
    /// não faz parte do molde) num arquivo de prefab reutilizável e linka esta entidade a ele.</summary>
    public void SaveAsPrefab(string filePath)
    {
        WritePrefabFile(filePath);

        string? assetsRoot = _owner?.Document?.AssetsRoot;
        PrefabPath = assetsRoot is null
            ? filePath
            : Path.GetRelativePath(assetsRoot, filePath).Replace('\\', '/');
        Edited?.Invoke($"prefablink:{Node.GetHashCode()}");
    }

    /// <summary>
    /// Substitui os componentes desta entidade pelos da prefab linkada — preserva o Transform
    /// (posição/rotação/escala são por instância, não fazem parte do "molde" reutilizável).
    /// </summary>
    public bool SyncFromPrefab()
    {
        string? assetsRoot = _owner?.Document?.AssetsRoot;
        if (PrefabPath is not { } rel || assetsRoot is null)
            return false;

        string full = Path.Combine(assetsRoot, rel);
        if (!File.Exists(full))
            return false;

        if (JsonNode.Parse(File.ReadAllText(full)) is not JsonObject prefabRoot
            || prefabRoot["Components"] is not JsonArray prefabComponents)
            return false;

        var transformNode = Transform?.Node;
        var newComponents = new JsonArray();
        if (transformNode is not null)
            newComponents.Add(JsonNode.Parse(transformNode.ToJsonString()));

        foreach (var comp in prefabComponents)
        {
            if (comp is JsonObject obj && obj["Type"]?.GetValue<string>() == "Transform")
                continue;
            newComponents.Add(JsonNode.Parse(comp!.ToJsonString()));
        }

        Node["Components"] = newComponents;
        RebuildComponents();
        Edited?.Invoke($"prefabsync:{Node.GetHashCode()}");
        return true;
    }

    /// <summary>Escreve os componentes atuais (exceto Transform) de volta no arquivo da prefab
    /// linkada, pra outras instâncias puxarem via <see cref="SyncFromPrefab"/>.</summary>
    public bool ApplyToPrefab()
    {
        string? assetsRoot = _owner?.Document?.AssetsRoot;
        if (PrefabPath is not { } rel || assetsRoot is null)
            return false;

        WritePrefabFile(Path.Combine(assetsRoot, rel));
        return true;
    }

    /// <summary>Grava Name + Components desta entidade (exceto Transform) no arquivo indicado —
    /// usado tanto por <see cref="SaveAsPrefab"/> quanto por <see cref="ApplyToPrefab"/>.</summary>
    private void WritePrefabFile(string filePath)
    {
        var componentsToSave = new JsonArray();
        foreach (var vm in Components)
        {
            if (vm.Type == "Transform")
                continue;
            componentsToSave.Add(JsonNode.Parse(vm.Node.ToJsonString()));
        }

        var prefabRoot = new JsonObject { ["Name"] = Name, ["Components"] = componentsToSave };
        File.WriteAllText(filePath, prefabRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
            "Health" => new JsonObject
            {
                ["Type"] = "Health",
                ["Max"] = 100f,
                ["Current"] = 100f,
            },
            "Projectile" => new JsonObject
            {
                ["Type"] = "Projectile",
                ["Life"] = 2f,
                ["Damage"] = 20f,
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
            "UiText" => new JsonObject
            {
                ["Type"] = "UiText",
                ["X"] = 20f,
                ["Y"] = 20f,
                ["Text"] = "Texto",
                ["Color"] = "#FFFFFFFF",
            },
            "UiImage" => new JsonObject
            {
                ["Type"] = "UiImage",
                ["X"] = 20f,
                ["Y"] = 20f,
                ["Width"] = 32f,
                ["Height"] = 32f,
            },
            "UiBar" => new JsonObject
            {
                ["Type"] = "UiBar",
                ["X"] = 20f,
                ["Y"] = 20f,
                ["Width"] = 150f,
                ["Height"] = 12f,
                ["Max"] = 100f,
            },
            "UiPanel" => new JsonObject
            {
                ["Type"] = "UiPanel",
                ["X"] = 10f,
                ["Y"] = 10f,
                ["Width"] = 200f,
                ["Height"] = 100f,
                ["Color"] = "#000000A0",
            },
            "UiButton" => new JsonObject
            {
                ["Type"] = "UiButton",
                ["X"] = 20f,
                ["Y"] = 20f,
                ["Width"] = 120f,
                ["Height"] = 32f,
                ["Text"] = "Botão",
                ["OnClick"] = new JsonArray(),
            },
            "UiJoystick" => new JsonObject
            {
                ["Type"] = "UiJoystick",
                ["X"] = 140f,
                ["Y"] = 140f,
                ["AnchorX"] = "Left",
                ["AnchorY"] = "Bottom",
                ["Radius"] = 70f,
            },
            "ParticleEmitter" => new JsonObject
            {
                ["Type"] = "ParticleEmitter",
                ["Rate"] = 10f,
                ["LifeMin"] = 0.6f,
                ["LifeMax"] = 1.2f,
                ["SpeedMin"] = 20f,
                ["SpeedMax"] = 60f,
                ["SizeStart"] = 8f,
                ["SizeEnd"] = 0f,
                ["ColorStart"] = "#FFFFFFFF",
                ["ColorEnd"] = "#FFFFFF00",
            },
            "Light2D" => new JsonObject
            {
                ["Type"] = "Light2D",
                ["Radius"] = 100f,
                ["Color"] = "#FFDC96FF",
                ["Intensity"] = 1f,
            },
            "NavAgent" => new JsonObject
            {
                ["Type"] = "NavAgent",
                ["Speed"] = 100f,
                ["ArriveThreshold"] = 4f,
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

    /// <summary>Move um elemento de UI (arrasto no canvas) — X/Y de pixel de tela direto no
    /// componente (UiButton/UiPanel/UiText/…), não no Transform da entidade.</summary>
    public void SetUiPosition(ComponentViewModel component, float x, float y)
    {
        component.Node["X"] = x;
        component.Node["Y"] = y;
        component.Number("X")?.RefreshFromNode();
        component.Number("Y")?.RefreshFromNode();
        Edited?.Invoke($"moveui:{Node.GetHashCode()}/{component.Node.GetHashCode()}");
    }

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

    private void RebuildComponents()
    {
        Components.Clear();
        if (Node["Components"] is JsonArray components)
        {
            foreach (var componentNode in components.OfType<JsonObject>())
                AddVm(BuildVm(componentNode));
        }
    }

    private ComponentViewModel BuildVm(JsonObject node) =>
        node["Type"]?.GetValue<string>() switch
        {
            "EventTrigger" => new EventTriggerViewModel(node, _owner),
            "Animator"     => new AnimatorViewModel(node),
            "UiButton"     => new UiButtonViewModel(node, _owner),
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
