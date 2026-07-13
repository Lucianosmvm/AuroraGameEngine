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
    public string AssetsRoot { get; private set; }

    public string SceneName => Root["Scene"]?.GetValue<string>() ?? "Sem nome";

    public JsonArray Objects => Root["Objects"] as JsonArray
        ?? throw new InvalidDataException("Cena sem array 'Objects'.");

    /// <summary>
    /// Redefine o assets root e persiste o caminho relativo no JSON da cena.
    /// Passa <paramref name="absolutePath"/> = pasta do arquivo para limpar o campo.
    /// </summary>
    public void SetAssetsRoot(string absolutePath)
    {
        string sceneDir = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        string rel = Path.GetRelativePath(sceneDir, absolutePath);
        AssetsRoot = absolutePath;

        if (rel == ".")
            Root.Remove("AssetsRoot");
        else
            Root["AssetsRoot"] = rel;
    }

    private SceneDocument(JsonObject root, string filePath, string assetsRoot)
    {
        Root = root;
        FilePath = filePath;
        AssetsRoot = assetsRoot;
    }

    /// <summary>Cria e salva uma cena vazia no caminho indicado.</summary>
    public static SceneDocument New(string filePath)
    {
        string sceneName = Path.GetFileNameWithoutExtension(filePath);
        var root = new JsonObject
        {
            ["Scene"] = sceneName,
            ["Objects"] = new JsonArray(),
        };
        string assetsRoot = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        var doc = new SceneDocument(root, filePath, assetsRoot);
        doc.Save();
        return doc;
    }

    public static SceneDocument Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException($"'{path}' não é um objeto JSON de cena.");

        return new SceneDocument(root, path, ResolveAssetsRoot(path, root));
    }

    /// <summary>Reconstrói o documento a partir de um snapshot (undo/redo).</summary>
    public static SceneDocument FromJson(string json, string filePath, string assetsRoot)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Snapshot de cena inválido.");

        return new SceneDocument(root, filePath, assetsRoot);
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

        // Campo explícito tem prioridade absoluta.
        if (root["AssetsRoot"]?.GetValue<string>() is { Length: > 0 } rel)
        {
            string resolved = Path.GetFullPath(Path.Combine(sceneDir, rel));
            if (Directory.Exists(resolved))
                return resolved;
        }

        // Fallback heurístico: sobe a partir da pasta do arquivo até achar uma textura.
        string? firstTexture = (root["Objects"] as JsonArray)?
            .OfType<JsonObject>()
            .SelectMany(o => o["Components"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(c => c["Texture"]?.GetValue<string>())
            .FirstOrDefault(t => t is not null);

        if (firstTexture is null)
            return FindAssetsRootViaProjectFile(sceneDir) ?? sceneDir;

        for (var dir = new DirectoryInfo(sceneDir); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, firstTexture)))
                return dir.FullName;
        }

        return FindAssetsRootViaProjectFile(sceneDir) ?? sceneDir;
    }

    /// <summary>
    /// Fallback quando não há textura pra guiar a heurística (cena/tela de UI sem imagem
    /// nenhuma, ex.: tela de UI só com UiText/UiBar): sobe procurando aurora.project.json —
    /// mesma convenção que ProjectSettings.Find já usa — e usa a pasta "Assets" ao lado dele.
    /// Sem isso, uma tela de UI sem imagem "esconderia" CENAS/PREFABS/outras telas do resto
    /// do projeto (AssetsRoot cairia na própria pasta da tela).
    /// </summary>
    private static string? FindAssetsRootViaProjectFile(string sceneDir)
    {
        for (var dir = new DirectoryInfo(sceneDir); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "aurora.project.json")))
                continue;

            string assets = Path.Combine(dir.FullName, "Assets");
            return Directory.Exists(assets) ? assets : dir.FullName;
        }
        return null;
    }
}
