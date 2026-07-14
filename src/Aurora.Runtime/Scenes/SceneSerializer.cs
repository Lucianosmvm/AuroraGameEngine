using System.Reflection;
using System.Text;
using System.Text.Json;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Events;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Scenes;

/// <summary>Serviços disponíveis durante load/save de cena.</summary>
public sealed class SceneContext
{
    public required World World { get; init; }
    public AssetManager? Assets { get; init; }
}

/// <summary>Lê um componente do JSON. Recebe o objeto do componente (com "Type" e demais campos).</summary>
public delegate IComponent ComponentReader(JsonElement json, SceneContext context);

/// <summary>Escreve os campos do componente (o objeto e o campo "Type" já foram abertos).</summary>
public delegate void ComponentWriter(Utf8JsonWriter json, IComponent component, SceneContext context);

/// <summary>
/// Converte cenas entre JSON e entidades no <see cref="World"/>.
/// Componentes são registrados por nome — o mesmo mecanismo serve para componentes
/// da engine, do jogo e, no futuro, de plugins.
/// Componentes sem writer registrado ficam de fora do save.
/// </summary>
public sealed class SceneSerializer
{
    private readonly Dictionary<string, ComponentReader> _readers = new();
    private readonly Dictionary<Type, (string Name, ComponentWriter Writer)> _writers = new();

    public SceneSerializer()
    {
        RegisterBuiltIns();
    }

    public void Register<T>(string typeName, ComponentReader reader, ComponentWriter? writer = null)
        where T : class, IComponent
    {
        _readers[typeName] = reader;
        if (writer is not null)
            _writers[typeof(T)] = (typeName, writer);
    }

    /// <summary>Um campo público exposto por um [SceneScript] — nome, tipo ("float"/"int"/"bool"/"string")
    /// e o valor default de verdade da classe (não um chute por tipo — <c>Enabled</c> herdado de
    /// <see cref="Behavior"/>, por exemplo, é <c>true</c>, não <c>false</c>).</summary>
    public sealed record ScriptFieldInfo(string Name, string Kind, string Default);

    /// <summary>Um [SceneScript] descoberto: nome do "Type" na cena + campos editáveis.</summary>
    public sealed record ScriptInfo(string Name, List<ScriptFieldInfo> Fields);

    /// <summary>
    /// Varre os assemblies por classes [SceneScript] e descreve nome + campos (com default real,
    /// instanciando a classe), sem registrar nada. Usado pelo editor (via <c>--describe-scripts</c>)
    /// pra listar scripts custom no "+Add Componente" sem precisar abrir o jogo.
    /// </summary>
    public List<ScriptInfo> DescribeScripts(params Assembly[] assemblies)
    {
        var result = new List<ScriptInfo>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<SceneScriptAttribute>();
                if (attr is null || type.IsAbstract || !typeof(Behavior).IsAssignableFrom(type))
                    continue;

                object? instance = null;
                try { instance = Activator.CreateInstance(type); }
                catch { /* sem construtor padrão acessível — segue sem defaults (tudo "0"/"false"/"") */ }

                var fields = GetScriptableMembers(type)
                    .Select(m => new ScriptFieldInfo(m.Name, KindName(m.Type),
                        FormatDefault(instance is null ? null : m.GetValue(instance))))
                    .ToList();
                result.Add(new ScriptInfo(attr.Name ?? type.Name, fields));
            }
        }
        return result;
    }

    private static string FormatDefault(object? value) => value switch
    {
        float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => s,
        _ => "",
    };

    private static string KindName(Type t) =>
        t == typeof(float) ? "float"
        : t == typeof(int) ? "int"
        : t == typeof(bool) ? "bool"
        : "string";

    private static readonly HashSet<Type> ScriptableFieldTypes = [typeof(float), typeof(int), typeof(bool), typeof(string)];

    /// <summary>
    /// Varre os assemblies em busca de classes marcadas com <see cref="SceneScriptAttribute"/>
    /// e registra cada uma automaticamente (leitura/escrita via reflection sobre campos e
    /// propriedades públicas float/int/bool/string). Chamado por <see cref="Game.AutoRegisterScripts"/>
    /// — não precisa chamar na mão.
    /// </summary>
    public void RegisterScripts(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attr = type.GetCustomAttribute<SceneScriptAttribute>();
                if (attr is null || type.IsAbstract || !typeof(Behavior).IsAssignableFrom(type))
                    continue;

                RegisterReflective(type, attr.Name ?? type.Name);
            }
        }
    }

    private void RegisterReflective(Type type, string name)
    {
        var members = GetScriptableMembers(type);

        _readers[name] = (json, _) =>
        {
            var instance = (IComponent)Activator.CreateInstance(type)!;
            foreach (var member in members)
            {
                if (!json.TryGetProperty(member.Name, out var prop))
                    continue;

                object? value = member.Type == typeof(float) ? prop.GetSingle()
                    : member.Type == typeof(int) ? prop.GetInt32()
                    : member.Type == typeof(bool) ? prop.GetBoolean()
                    : prop.GetString();

                member.SetValue(instance, value);
            }
            return instance;
        };

        _writers[type] = (name, (json, component, _) =>
        {
            foreach (var member in members)
            {
                switch (member.GetValue(component))
                {
                    case float f: json.WriteNumber(member.Name, f); break;
                    case int i: json.WriteNumber(member.Name, i); break;
                    case bool b: json.WriteBoolean(member.Name, b); break;
                    case string s: json.WriteString(member.Name, s); break;
                }
            }
        });
    }

    private readonly record struct ScriptMember(string Name, Type Type,
        Func<object, object?> GetValue, Action<object, object?> SetValue);

    private static List<ScriptMember> GetScriptableMembers(Type type)
    {
        var result = new List<ScriptMember>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!ScriptableFieldTypes.Contains(field.FieldType))
                continue;
            result.Add(new ScriptMember(field.Name, field.FieldType,
                field.GetValue!, field.SetValue!));
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!ScriptableFieldTypes.Contains(prop.PropertyType) || !prop.CanRead || !prop.CanWrite
                || prop.GetIndexParameters().Length > 0)
                continue;
            result.Add(new ScriptMember(prop.Name, prop.PropertyType,
                prop.GetValue!, prop.SetValue!));
        }

        return result;
    }

    /// <summary>Cria as entidades do JSON dentro de <paramref name="context"/>.World.</summary>
    public void Load(string json, SceneContext context)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        foreach (var objectElement in root.GetProperty("Objects").EnumerateArray())
        {
            string name = objectElement.TryGetProperty("Name", out var nameProp)
                ? nameProp.GetString() ?? "Entity"
                : "Entity";

            var entity = context.World.CreateEntity(name);

            if (!objectElement.TryGetProperty("Components", out var components))
                continue;

            foreach (var componentElement in components.EnumerateArray())
            {
                string typeName = componentElement.GetProperty("Type").GetString()
                    ?? throw new InvalidDataException($"Componente sem 'Type' na entidade '{name}'.");

                if (!_readers.TryGetValue(typeName, out var reader))
                {
                    // Não derruba o load da cena inteira por um componente não registrado
                    // (ex: UiText/UiButton/UiPanel/UiBar/UiImage só existem em telas de UI,
                    // carregadas via UIManager.Load — não são IComponent aqui) — loga e pula.
                    Console.Error.WriteLine(
                        $"[SceneSerializer] Componente '{typeName}' (entidade '{name}') não registrado — ignorado. " +
                        $"Registrados: {string.Join(", ", _readers.Keys)}");
                    continue;
                }

                entity.Add(reader(componentElement, context));
            }
        }
    }

    /// <summary>Serializa todas as entidades do mundo para JSON.</summary>
    public string Save(string sceneName, SceneContext context)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("Scene", sceneName);
            json.WriteStartArray("Objects");

            foreach (var entity in context.World.Entities)
            {
                json.WriteStartObject();
                json.WriteString("Name", entity.Name);
                json.WriteStartArray("Components");

                foreach (var component in context.World.GetComponents(entity.Id))
                {
                    if (!_writers.TryGetValue(component.GetType(), out var entry))
                        continue;

                    json.WriteStartObject();
                    json.WriteString("Type", entry.Name);
                    entry.Writer(json, component, context);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void RegisterBuiltIns()
    {
        RegisterAnimator();
        RegisterCollider();
        RegisterHealth();
        RegisterProjectile();
        RegisterCameraController();
        RegisterParticleEmitter();
        RegisterLight2D();
        RegisterNavAgent();
        Register<Transform>("Transform",
            static (json, _) => new Transform
            {
                Position = new(GetFloat(json, "X", 0f), GetFloat(json, "Y", 0f)),
                Rotation = GetFloat(json, "Rotation", 0f),
                Scale = new(GetFloat(json, "ScaleX", 1f), GetFloat(json, "ScaleY", 1f)),
            },
            static (json, component, _) =>
            {
                var t = (Transform)component;
                json.WriteNumber("X", t.Position.X);
                json.WriteNumber("Y", t.Position.Y);
                if (t.Rotation != 0f)
                    json.WriteNumber("Rotation", t.Rotation);
                if (t.Scale.X != 1f || t.Scale.Y != 1f)
                {
                    json.WriteNumber("ScaleX", t.Scale.X);
                    json.WriteNumber("ScaleY", t.Scale.Y);
                }
            });

        Register<SpriteRenderer>("SpriteRenderer",
            static (json, context) =>
            {
                var sprite = new SpriteRenderer
                {
                    Layer = GetInt(json, "Layer", 0),
                    Origin = new(GetFloat(json, "OriginX", 0.5f), GetFloat(json, "OriginY", 0.5f)),
                    FlipX = GetBool(json, "FlipX", false),
                    FlipY = GetBool(json, "FlipY", false),
                    Visible = GetBool(json, "Visible", true),
                };

                if (json.TryGetProperty("Color", out var color))
                    sprite.Color = Color.FromHex(color.GetString()!);

                if (json.TryGetProperty("Texture", out var texture))
                {
                    string path = texture.GetString()!;
                    sprite.Texture = context.Assets?.LoadTexture(path)
                        ?? throw new InvalidOperationException(
                            $"Cena referencia textura '{path}' mas o contexto não tem AssetManager.");
                }

                return sprite;
            },
            static (json, component, context) =>
            {
                var s = (SpriteRenderer)component;
                if (s.Texture is not null && context.Assets?.GetTexturePath(s.Texture) is { } path)
                    json.WriteString("Texture", path);
                if (s.Layer != 0)
                    json.WriteNumber("Layer", s.Layer);
                if (s.Origin.X != 0.5f || s.Origin.Y != 0.5f)
                {
                    json.WriteNumber("OriginX", s.Origin.X);
                    json.WriteNumber("OriginY", s.Origin.Y);
                }
                if (s.FlipX) json.WriteBoolean("FlipX", true);
                if (s.FlipY) json.WriteBoolean("FlipY", true);
                if (!s.Visible) json.WriteBoolean("Visible", false);
                if (s.Color.ToHex() != "#FFFFFFFF")
                    json.WriteString("Color", s.Color.ToHex());
            });

        Register<Tilemap>("Tilemap",
            static (json, context) =>
            {
                var map = new Tilemap
                {
                    TileWidth = GetInt(json, "TileWidth", 16),
                    TileHeight = GetInt(json, "TileHeight", 16),
                    Width = GetInt(json, "Width", 0),
                    Height = GetInt(json, "Height", 0),
                    Layer = GetInt(json, "Layer", 0),
                };

                if (json.TryGetProperty("Texture", out var texture))
                {
                    string path = texture.GetString()!;
                    map.Tileset = context.Assets?.LoadTexture(path)
                        ?? throw new InvalidOperationException(
                            $"Cena referencia tileset '{path}' mas o contexto não tem AssetManager.");
                }

                if (json.TryGetProperty("Tiles", out var tiles))
                    map.Tiles = tiles.EnumerateArray().Select(t => t.GetInt32()).ToArray();

                // SolidTiles: aceita string "1, 3, 5" (editor) ou array [1, 3, 5]
                if (json.TryGetProperty("SolidTiles", out var solidEl))
                {
                    if (solidEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        foreach (var part in (solidEl.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (int.TryParse(part, out int idx))
                                map.SolidTiles.Add(idx);
                    }
                    else if (solidEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var t in solidEl.EnumerateArray())
                            map.SolidTiles.Add(t.GetInt32());
                    }
                }

                map.EnsureSize();
                return map;
            },
            static (json, component, context) =>
            {
                var map = (Tilemap)component;
                if (map.Tileset is not null && context.Assets?.GetTexturePath(map.Tileset) is { } path)
                    json.WriteString("Texture", path);
                json.WriteNumber("TileWidth", map.TileWidth);
                json.WriteNumber("TileHeight", map.TileHeight);
                json.WriteNumber("Width", map.Width);
                json.WriteNumber("Height", map.Height);
                if (map.Layer != 0)
                    json.WriteNumber("Layer", map.Layer);

                if (map.SolidTiles.Count > 0)
                    json.WriteString("SolidTiles",
                        string.Join(", ", map.SolidTiles.OrderBy(x => x)));

                json.WriteStartArray("Tiles");
                foreach (int tile in map.Tiles)
                    json.WriteNumberValue(tile);
                json.WriteEndArray();
            });

        Register<EventTrigger>("EventTrigger",
            static (json, _) =>
            {
                var trigger = new EventTrigger
                {
                    Trigger      = GetString(json, "Trigger", "PlayerTouch"),
                    Switch       = json.TryGetProperty("Switch", out var sw) ? sw.GetString() : null,
                    Radius       = GetFloat(json, "Radius", 20f),
                    Key          = GetString(json, "Key", "E"),
                    Interval     = GetFloat(json, "Interval", 5f),
                    Variable     = json.TryGetProperty("Variable", out var varProp) ? varProp.GetString() : null,
                    CompareOp    = GetString(json, "CompareOp", ">="),
                    CompareValue = GetFloat(json, "CompareValue", 0f),
                    Once         = GetBool(json, "Once", true),
                };

                if (json.TryGetProperty("Actions", out var actions))
                    trigger.Actions = EventAction.ParseList(actions);

                return trigger;
            },
            static (json, component, _) =>
            {
                var trigger = (EventTrigger)component;
                json.WriteString("Trigger", trigger.Trigger);
                if (trigger.Switch is not null)
                    json.WriteString("Switch", trigger.Switch);
                if (trigger.Radius != 20f)
                    json.WriteNumber("Radius", trigger.Radius);
                if (trigger.Key != "E")
                    json.WriteString("Key", trigger.Key);
                if (trigger.Interval != 5f)
                    json.WriteNumber("Interval", trigger.Interval);
                if (trigger.Variable is not null)
                    json.WriteString("Variable", trigger.Variable);
                if (trigger.CompareOp != ">=")
                    json.WriteString("CompareOp", trigger.CompareOp);
                if (trigger.CompareValue != 0f)
                    json.WriteNumber("CompareValue", trigger.CompareValue);
                if (!trigger.Once)
                    json.WriteBoolean("Once", false);

                EventAction.WriteList(json, "Actions", trigger.Actions);
            });
    }

    private void RegisterHealth()
    {
        Register<Health>("Health",
            static (json, _) =>
            {
                float max = GetFloat(json, "Max", 100f);
                return new Health
                {
                    Max = max,
                    Current = GetFloat(json, "Current", max),
                    InvulnerabilityAfterHit = GetFloat(json, "InvulnerabilityAfterHit", 0f),
                    Invulnerable = GetBool(json, "Invulnerable", false),
                    DestroyOnDeath = GetBool(json, "DestroyOnDeath", true),
                };
            },
            static (json, component, _) =>
            {
                var h = (Health)component;
                json.WriteNumber("Max", h.Max);
                if (h.Current != h.Max) json.WriteNumber("Current", h.Current);
                if (h.InvulnerabilityAfterHit != 0f) json.WriteNumber("InvulnerabilityAfterHit", h.InvulnerabilityAfterHit);
                if (h.Invulnerable) json.WriteBoolean("Invulnerable", true);
                if (!h.DestroyOnDeath) json.WriteBoolean("DestroyOnDeath", false);
            });
    }

    private void RegisterProjectile()
    {
        // Velocity/Source são runtime (setados no spawn em código) — só Life/Damage/TargetPrefix
        // fazem sentido autorar numa cena estática (prefab de projétil, por exemplo).
        Register<Projectile>("Projectile",
            static (json, _) => new Projectile
            {
                Life = GetFloat(json, "Life", 2f),
                Damage = GetFloat(json, "Damage", 20f),
                TargetPrefix = GetString(json, "TargetPrefix", ""),
            },
            static (json, component, _) =>
            {
                var p = (Projectile)component;
                if (p.Life != 2f) json.WriteNumber("Life", p.Life);
                if (p.Damage != 20f) json.WriteNumber("Damage", p.Damage);
                if (p.TargetPrefix.Length > 0) json.WriteString("TargetPrefix", p.TargetPrefix);
            });
    }

    private void RegisterCollider()
    {
        Register<Collider>("Collider",
            static (json, _) => new Collider
            {
                Shape = GetString(json, "Shape", "Box") == "Circle" ? ColliderShape.Circle : ColliderShape.Box,
                Width = GetFloat(json, "Width", 16f),
                Height = GetFloat(json, "Height", 16f),
                Radius = GetFloat(json, "Radius", 8f),
                Offset = new(GetFloat(json, "OffsetX", 0f), GetFloat(json, "OffsetY", 0f)),
                IsSolid = GetBool(json, "IsSolid", true),
                IsKinematic = GetBool(json, "IsKinematic", false),
                Layer = GetInt(json, "Layer", 1),
                Mask = GetInt(json, "Mask", ~0),
            },
            static (json, component, _) =>
            {
                var c = (Collider)component;
                if (c.Shape == ColliderShape.Circle) json.WriteString("Shape", "Circle");
                json.WriteNumber("Width", c.Width);
                json.WriteNumber("Height", c.Height);
                if (c.Shape == ColliderShape.Circle) json.WriteNumber("Radius", c.Radius);
                if (c.Offset.X != 0f || c.Offset.Y != 0f)
                {
                    json.WriteNumber("OffsetX", c.Offset.X);
                    json.WriteNumber("OffsetY", c.Offset.Y);
                }
                if (!c.IsSolid) json.WriteBoolean("IsSolid", false);
                if (c.IsKinematic) json.WriteBoolean("IsKinematic", true);
                if (c.Layer != 1) json.WriteNumber("Layer", c.Layer);
                if (c.Mask != ~0) json.WriteNumber("Mask", c.Mask);
            });
    }

    private void RegisterAnimator()
    {
        Register<Animator>("Animator",
            static (json, _) =>
            {
                var animator = new Animator
                {
                    FrameWidth = GetInt(json, "FrameWidth", 0),
                    FrameHeight = GetInt(json, "FrameHeight", 0),
                    SheetColumns = GetInt(json, "SheetColumns", 1),
                };

                if (json.TryGetProperty("Clips", out var clipsEl))
                {
                    foreach (var clipEl in clipsEl.EnumerateArray())
                    {
                        var clip = new AnimationClip
                        {
                            Name = GetString(clipEl, "Name", ""),
                            FrameDuration = GetFloat(clipEl, "Duration", 0.1f),
                            Loop = GetBool(clipEl, "Loop", true),
                        };

                        if (clipEl.TryGetProperty("Frames", out var framesEl))
                            clip.Frames = framesEl.EnumerateArray().Select(f => f.GetInt32()).ToArray();

                        animator.Clips.Add(clip);
                    }
                }

                if (json.TryGetProperty("Transitions", out var transitionsEl))
                {
                    foreach (var transEl in transitionsEl.EnumerateArray())
                    {
                        animator.Transitions.Add(new AnimatorTransition
                        {
                            From = GetString(transEl, "From", "Any"),
                            To = GetString(transEl, "To", ""),
                            Parameter = GetString(transEl, "Parameter", ""),
                            IsBool = GetBool(transEl, "IsBool", false),
                            CompareOp = GetString(transEl, "CompareOp", ">="),
                            CompareValue = GetFloat(transEl, "CompareValue", 0f),
                            BoolValue = GetBool(transEl, "BoolValue", true),
                        });
                    }
                }

                return animator;
            },
            static (json, component, _) =>
            {
                var a = (Animator)component;
                json.WriteNumber("FrameWidth", a.FrameWidth);
                json.WriteNumber("FrameHeight", a.FrameHeight);
                if (a.SheetColumns != 1) json.WriteNumber("SheetColumns", a.SheetColumns);

                json.WriteStartArray("Clips");
                foreach (var clip in a.Clips)
                {
                    json.WriteStartObject();
                    json.WriteString("Name", clip.Name);
                    json.WriteNumber("Duration", clip.FrameDuration);
                    if (!clip.Loop) json.WriteBoolean("Loop", false);

                    json.WriteStartArray("Frames");
                    foreach (int f in clip.Frames) json.WriteNumberValue(f);
                    json.WriteEndArray();

                    json.WriteEndObject();
                }
                json.WriteEndArray();

                if (a.Transitions.Count > 0)
                {
                    json.WriteStartArray("Transitions");
                    foreach (var t in a.Transitions)
                    {
                        json.WriteStartObject();
                        json.WriteString("From", t.From);
                        json.WriteString("To", t.To);
                        json.WriteString("Parameter", t.Parameter);
                        if (t.IsBool) json.WriteBoolean("IsBool", true);
                        if (t.CompareOp != ">=") json.WriteString("CompareOp", t.CompareOp);
                        if (t.CompareValue != 0f) json.WriteNumber("CompareValue", t.CompareValue);
                        if (!t.BoolValue) json.WriteBoolean("BoolValue", false);
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                }
            });
    }

    private void RegisterCameraController()
    {
        Register<CameraController>("CameraController",
            static (json, _) => new CameraController
            {
                Follow      = json.TryGetProperty("Follow", out var f) ? f.GetString() : null,
                FollowSpeed = GetFloat(json, "FollowSpeed", 5f),
                Zoom        = GetFloat(json, "Zoom", 1f),
                Offset      = new(GetFloat(json, "OffsetX", 0f), GetFloat(json, "OffsetY", 0f)),
                ViewWidth   = GetInt(json, "ViewWidth", 1280),
                ViewHeight  = GetInt(json, "ViewHeight", 720),
                ClampBounds = GetBool(json, "ClampBounds", false),
                BoundsX     = GetFloat(json, "BoundsX", 0f),
                BoundsY     = GetFloat(json, "BoundsY", 0f),
                BoundsWidth = GetFloat(json, "BoundsWidth", 1280f),
                BoundsHeight= GetFloat(json, "BoundsHeight", 720f),
            },
            static (json, component, _) =>
            {
                var c = (CameraController)component;
                if (c.Follow is not null)    json.WriteString("Follow", c.Follow);
                if (c.FollowSpeed != 5f)     json.WriteNumber("FollowSpeed", c.FollowSpeed);
                if (c.Zoom != 1f)            json.WriteNumber("Zoom", c.Zoom);
                if (c.Offset.X != 0f)        json.WriteNumber("OffsetX", c.Offset.X);
                if (c.Offset.Y != 0f)        json.WriteNumber("OffsetY", c.Offset.Y);
                if (c.ViewWidth != 1280)     json.WriteNumber("ViewWidth", c.ViewWidth);
                if (c.ViewHeight != 720)     json.WriteNumber("ViewHeight", c.ViewHeight);
                if (c.ClampBounds)
                {
                    json.WriteBoolean("ClampBounds", true);
                    json.WriteNumber("BoundsX", c.BoundsX);
                    json.WriteNumber("BoundsY", c.BoundsY);
                    json.WriteNumber("BoundsWidth", c.BoundsWidth);
                    json.WriteNumber("BoundsHeight", c.BoundsHeight);
                }
            });
    }

    private void RegisterParticleEmitter()
    {
        Register<ParticleEmitter>("ParticleEmitter",
            static (json, context) =>
            {
                var emitter = new ParticleEmitter
                {
                    Rate = GetFloat(json, "Rate", 10f),
                    Emitting = GetBool(json, "Emitting", true),
                    LifeMin = GetFloat(json, "LifeMin", 0.6f),
                    LifeMax = GetFloat(json, "LifeMax", 1.2f),
                    SpeedMin = GetFloat(json, "SpeedMin", 20f),
                    SpeedMax = GetFloat(json, "SpeedMax", 60f),
                    AngleMin = GetFloat(json, "AngleMin", 0f),
                    AngleMax = GetFloat(json, "AngleMax", 360f),
                    SizeStart = GetFloat(json, "SizeStart", 8f),
                    SizeEnd = GetFloat(json, "SizeEnd", 0f),
                    Gravity = new(GetFloat(json, "GravityX", 0f), GetFloat(json, "GravityY", 0f)),
                    Layer = GetInt(json, "Layer", 0),
                    MaxParticles = GetInt(json, "MaxParticles", 200),
                };

                if (json.TryGetProperty("ColorStart", out var cs))
                    emitter.ColorStart = Color.FromHex(cs.GetString()!);
                if (json.TryGetProperty("ColorEnd", out var ce))
                    emitter.ColorEnd = Color.FromHex(ce.GetString()!);

                if (json.TryGetProperty("Texture", out var texture))
                {
                    string path = texture.GetString()!;
                    emitter.Texture = context.Assets?.LoadTexture(path)
                        ?? throw new InvalidOperationException(
                            $"Cena referencia textura '{path}' mas o contexto não tem AssetManager.");
                }

                return emitter;
            },
            static (json, component, context) =>
            {
                var e = (ParticleEmitter)component;
                if (e.Texture is not null && context.Assets?.GetTexturePath(e.Texture) is { } path)
                    json.WriteString("Texture", path);
                if (e.Rate != 10f) json.WriteNumber("Rate", e.Rate);
                if (!e.Emitting) json.WriteBoolean("Emitting", false);
                if (e.LifeMin != 0.6f) json.WriteNumber("LifeMin", e.LifeMin);
                if (e.LifeMax != 1.2f) json.WriteNumber("LifeMax", e.LifeMax);
                if (e.SpeedMin != 20f) json.WriteNumber("SpeedMin", e.SpeedMin);
                if (e.SpeedMax != 60f) json.WriteNumber("SpeedMax", e.SpeedMax);
                if (e.AngleMin != 0f) json.WriteNumber("AngleMin", e.AngleMin);
                if (e.AngleMax != 360f) json.WriteNumber("AngleMax", e.AngleMax);
                if (e.SizeStart != 8f) json.WriteNumber("SizeStart", e.SizeStart);
                if (e.SizeEnd != 0f) json.WriteNumber("SizeEnd", e.SizeEnd);
                if (e.ColorStart.ToHex() != "#FFFFFFFF") json.WriteString("ColorStart", e.ColorStart.ToHex());
                if (e.ColorEnd.ToHex() != "#FFFFFF00") json.WriteString("ColorEnd", e.ColorEnd.ToHex());
                if (e.Gravity.X != 0f) json.WriteNumber("GravityX", e.Gravity.X);
                if (e.Gravity.Y != 0f) json.WriteNumber("GravityY", e.Gravity.Y);
                if (e.Layer != 0) json.WriteNumber("Layer", e.Layer);
                if (e.MaxParticles != 200) json.WriteNumber("MaxParticles", e.MaxParticles);
            });
    }

    private void RegisterLight2D()
    {
        Register<Light2D>("Light2D",
            static (json, _) => new Light2D
            {
                Radius = GetFloat(json, "Radius", 100f),
                Intensity = GetFloat(json, "Intensity", 1f),
                Enabled = GetBool(json, "Enabled", true),
                Color = json.TryGetProperty("Color", out var c)
                    ? Color.FromHex(c.GetString()!)
                    : Color.FromBytes(255, 220, 150),
            },
            static (json, component, _) =>
            {
                var l = (Light2D)component;
                if (l.Radius != 100f) json.WriteNumber("Radius", l.Radius);
                if (l.Intensity != 1f) json.WriteNumber("Intensity", l.Intensity);
                if (!l.Enabled) json.WriteBoolean("Enabled", false);
                if (l.Color.ToHex() != "#FFDC96FF") json.WriteString("Color", l.Color.ToHex());
            });
    }

    private void RegisterNavAgent()
    {
        // Target/Path/HasTarget são estado de runtime (setados via SetTarget em código) -
        // não fazem sentido persistidos numa cena estática, só Speed/ArriveThreshold.
        Register<NavAgent>("NavAgent",
            static (json, _) => new NavAgent
            {
                Speed = GetFloat(json, "Speed", 100f),
                ArriveThreshold = GetFloat(json, "ArriveThreshold", 4f),
            },
            static (json, component, _) =>
            {
                var a = (NavAgent)component;
                if (a.Speed != 100f) json.WriteNumber("Speed", a.Speed);
                if (a.ArriveThreshold != 4f) json.WriteNumber("ArriveThreshold", a.ArriveThreshold);
            });
    }

    public static float GetFloat(JsonElement json, string name, float fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetSingle() : fallback;

    public static int GetInt(JsonElement json, string name, int fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetInt32() : fallback;

    public static bool GetBool(JsonElement json, string name, bool fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetBoolean() : fallback;

    public static string GetString(JsonElement json, string name, string fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetString() ?? fallback : fallback;
}
