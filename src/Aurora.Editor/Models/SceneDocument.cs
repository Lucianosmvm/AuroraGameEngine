using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aurora.Editor.Models;

/// <summary>
/// Cena aberta no editor. Mantém o DOM JSON vivo: edições conhecidas mexem nos nós,
/// componentes/campos desconhecidos sobrevivem ao save intactos (base para plugins).
/// </summary>
public sealed class SceneDocument
{
    public JsonObject Root { get; }
    public string FilePath { get; private set; }

    /// <summary>Pasta raiz dos assets — caminhos de textura da cena são relativos a ela.</summary>
    public string AssetsRoot { get; }

    public string SceneName => Root["Scene"]?.GetValue<string>() ?? "Sem nome";

    public JsonArray Objects => Root["Objects"] as JsonArray
        ?? throw new InvalidDataException("Cena sem array 'Objects'.");

    private SceneDocument(JsonObject root, string filePath, string assetsRoot)
    {
        Root = root;
        FilePath = filePath;
        AssetsRoot = assetsRoot;
    }

    public static SceneDocument Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException($"'{path}' não é um objeto JSON de cena.");

        return new SceneDocument(root, path, ResolveAssetsRoot(path, root));
    }

    public void Save(string? asPath = null)
    {
        FilePath = asPath ?? FilePath;
        File.WriteAllText(FilePath, Root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Acha a pasta que resolve os caminhos de textura da cena: sobe a partir da pasta
    /// do arquivo até alguma textura referenciada existir (convenção: Assets/scenes/x.json
    /// com texturas "sprites/y.png" relativas a Assets/).
    /// </summary>
    private static string ResolveAssetsRoot(string scenePath, JsonObject root)
    {
        string sceneDir = Path.GetDirectoryName(Path.GetFullPath(scenePath))!;

        string? firstTexture = (root["Objects"] as JsonArray)?
            .OfType<JsonObject>()
            .SelectMany(o => o["Components"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(c => c["Texture"]?.GetValue<string>())
            .FirstOrDefault(t => t is not null);

        if (firstTexture is null)
            return sceneDir;

        for (var dir = new DirectoryInfo(sceneDir); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, firstTexture)))
                return dir.FullName;
        }

        return sceneDir;
    }
}
