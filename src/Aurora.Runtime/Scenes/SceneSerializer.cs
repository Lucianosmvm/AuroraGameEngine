using System.Text;
using System.Text.Json;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
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
                    throw new InvalidDataException(
                        $"Componente '{typeName}' (entidade '{name}') não registrado. " +
                        $"Registrados: {string.Join(", ", _readers.Keys)}");

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
    }

    public static float GetFloat(JsonElement json, string name, float fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetSingle() : fallback;

    public static int GetInt(JsonElement json, string name, int fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetInt32() : fallback;

    public static bool GetBool(JsonElement json, string name, bool fallback)
        => json.TryGetProperty(name, out var prop) ? prop.GetBoolean() : fallback;
}
