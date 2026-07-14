using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace Aurora.Editor.ViewModels;

/// <summary>Componente de uma entidade no inspector.</summary>
public class ComponentViewModel : ViewModelBase
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
        ["Tilemap"] =
        [
            ("Texture", ""), ("TileWidth", 16f), ("TileHeight", 16f),
            ("Width", 0f), ("Height", 0f), ("Layer", 0f),
            ("SolidTiles", ""),   // índices separados por vírgula, ex: "1, 3, 5"
        ],
        ["Animator"] =
        [
            ("FrameWidth", 0f), ("FrameHeight", 0f), ("SheetColumns", 1f),
        ],
        ["Collider"] =
        [
            ("Shape", "Box"), ("Width", 16f), ("Height", 16f), ("Radius", 8f),
            ("OffsetX", 0f), ("OffsetY", 0f),
            ("IsSolid", true), ("IsKinematic", false),
            ("Layer", 1f), ("Mask", -1f),
        ],
        // Vida — dano/cura em código via World.Damage/Heal, ou sem código via EventAction
        // "Damage"/"Heal" no EventTrigger.
        ["Health"] =
        [
            ("Max", 100f), ("Current", 100f), ("InvulnerabilityAfterHit", 0f),
            ("Invulnerable", false), ("DestroyOnDeath", true),
        ],
        // Ataque à distância — Velocity/Source são setados em código no spawn (não fazem
        // sentido numa cena estática), só aparecem os campos abaixo pra editar/prefab.
        ["Projectile"] =
        [
            ("Life", 2f), ("Damage", 20f), ("TargetPrefix", ""),
        ],
        ["CameraController"] =
        [
            ("Follow", ""),
            ("FollowSpeed", 5f), ("Zoom", 1f),
            ("OffsetX", 0f), ("OffsetY", 0f),
            ("ViewWidth", 1280f), ("ViewHeight", 720f),
            ("ClampBounds", false),
            ("BoundsX", 0f), ("BoundsY", 0f),
            ("BoundsWidth", 1280f), ("BoundsHeight", 720f),
        ],
        // Componentes de UI (HUD/menu): X/Y em pixel de tela, não seguem a câmera.
        // Texto suporta tokens {Var}, {Item:Nome}, {Quest:Nome} — ver Aurora.Runtime.UI.UIManager.
        // AnchorX/Y: "Left"/"Top" (padrão, X/Y é canto absoluto — bom pra HUD grudado no canto)
        // | "Center" (X/Y vira deslocamento a partir do centro da tela — bom pra menu, funciona
        // igual em qualquer resolução) | "Right"/"Bottom" (a partir da borda oposta).
        ["UiText"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Text", ""), ("Color", "#FFFFFFFF"), ("Scale", 1f),
        ],
        ["UiImage"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Texture", ""), ("Width", 0f), ("Height", 0f), ("Color", "#FFFFFFFF"),
        ],
        ["UiBar"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Width", 100f), ("Height", 12f),
            ("Variable", ""), ("Max", 100f),
            ("FillColor", "#40C040FF"), ("BackColor", "#303030FF"),
        ],
        ["UiPanel"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Width", 100f), ("Height", 100f), ("Color", "#000000AA"),
        ],
        // Botão clicável (mouse/toque) — ações em "OnClick" (editor: UiButtonViewModel).
        ["UiButton"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Top"),
            ("Width", 120f), ("Height", 32f), ("Text", "Botão"),
            ("Color", "#3A3860FF"), ("HoverColor", "#4A4880FF"), ("PressedColor", "#2A2850FF"),
            ("TextColor", "#FFFFFFFF"),
        ],
        // Joystick virtual (toque multi-dedo) — X/Y+Anchor definem o canto de um quadrado de
        // lado 2*Radius (mesma convenção de posição dos outros Ui*); centro fica no meio dele.
        // Leia UIManager.Find<UiJoystick>(tela, nome).Value em código pra mover o player.
        ["UiJoystick"] =
        [
            ("X", 0f), ("Y", 0f), ("AnchorX", "Left"), ("AnchorY", "Bottom"),
            ("Radius", 70f), ("BaseColor", "#FFFFFF2E"), ("KnobColor", "#FFFFFF66"),
        ],
        // Emissor de partículas (fumaça, faíscas, folhas) — sem Texture desenha quad colorido.
        ["ParticleEmitter"] =
        [
            ("Texture", ""), ("Rate", 10f), ("Emitting", true),
            ("LifeMin", 0.6f), ("LifeMax", 1.2f),
            ("SpeedMin", 20f), ("SpeedMax", 60f),
            ("AngleMin", 0f), ("AngleMax", 360f),
            ("SizeStart", 8f), ("SizeEnd", 0f),
            ("ColorStart", "#FFFFFFFF"), ("ColorEnd", "#FFFFFF00"),
            ("GravityX", 0f), ("GravityY", 0f),
            ("Layer", 0f), ("MaxParticles", 200f),
        ],
        // Luz 2D: brilho aditivo (glow), não é sombra/oclusão dinâmica.
        ["Light2D"] =
        [
            ("Radius", 100f), ("Color", "#FFDC96FF"), ("Intensity", 1f), ("Enabled", true),
        ],
        // Tinta multiplicativa de tela inteira: dia/noite, tempestade, filtro subaquático.
        // Liga/desliga em runtime via EventAction SetActive.
        ["GlobalTint"] =
        [
            ("Color", "#000028FF"), ("Intensity", 0.3f), ("Enabled", true),
        ],
        // NavAgent: alvo é setado em código (SetTarget), só Speed/ArriveThreshold são autoráveis na cena.
        ["NavAgent"] =
        [
            ("Speed", 100f), ("ArriveThreshold", 4f),
        ],
    };

    public JsonObject Node { get; }
    public string Type { get; }
    public List<PropertyViewModel> Properties { get; } = [];

    /// <summary>Definido por EntityViewModel para componentes removíveis (todos exceto Transform).</summary>
    public ICommand? RemoveCommand { get; internal set; }

    public event Action<string>? Edited;

    protected void RaiseEdited(string tag) => Edited?.Invoke($"{Type}.{tag}");

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
        // Arrays e objetos (ex.: Tiles do Tilemap) não viram editor de texto.
        foreach (var (name, value) in Node)
        {
            if (added.Contains(name) || value is null)
                continue;

            var kind = value.GetValueKind();
            if (kind is JsonValueKind.Array or JsonValueKind.Object)
                continue;

            object fallback = kind switch
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
    public float GetFloat(string name, float fallback) => PropertyViewModel.ReadFloat(Node[name],    fallback);
    public string? GetString(string name) => Node[name]?.GetValue<string>();
    public bool GetBool(string name, bool fallback) => Node[name]?.GetValue<bool>() ?? fallback;
}
