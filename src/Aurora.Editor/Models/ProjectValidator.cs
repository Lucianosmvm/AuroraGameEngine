using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aurora.Editor.Models;

/// <summary>
/// Varre as cenas do projeto procurando referência que não existe mais. Todo problema que ele
/// acha é do tipo que o compilador NÃO pega: o jogo compila, abre, e ou fecha sozinho (asset
/// faltando) ou roda calado sem o comportamento (componente não registrado — o SceneSerializer
/// só escreve uma linha no console e segue).
///
/// Puro de propósito: recebe caminhos e devolve lista, sem tocar em UI nem em processo. É o que
/// permite testar cada regra sem abrir o editor.
/// </summary>
public static class ProjectValidator
{
    /// <summary><paramref name="Where"/> é "cena › entidade" (ou só a cena) pra dar pra ir direto
    /// no lugar; <paramref name="Message"/> já diz o que fazer.</summary>
    public sealed record Problem(string Where, string Message);

    /// <summary>Campos de componente que guardam caminho de asset. "Texture" é o nome usado pelo
    /// SceneSerializer tanto no SpriteRenderer quanto no tileset do Tilemap e no UiImage.</summary>
    private static readonly string[] AssetFields = ["Texture"];

    /// <summary>Componentes que o editor conhece mas que NÃO são IComponent de cena: existem só
    /// em tela de UI (UIManager). Numa cena comum são ignorados com aviso — vale sinalizar.</summary>
    private static readonly HashSet<string> UiOnlyComponents =
        ["UiText", "UiImage", "UiBar", "UiPanel", "UiButton", "UiJoystick"];

    /// <summary>
    /// Roda todas as regras. <paramref name="knownComponents"/> deve trazer os componentes
    /// nativos MAIS os scripts [SceneScript] descobertos no projeto — sem os scripts, todo
    /// componente custom viraria falso positivo.
    /// </summary>
    public static IReadOnlyList<Problem> Validate(
        string assetsRoot,
        IEnumerable<string> sceneFiles,
        IReadOnlyCollection<string> knownComponents,
        string? uiFont)
    {
        var problems = new List<Problem>();
        var known = new HashSet<string>(knownComponents, StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(uiFont) && !AssetExists(assetsRoot, uiFont))
            problems.Add(new Problem("aurora.project.json",
                $"uiFont aponta pra '{uiFont}', que não existe em Assets/. O template do jogo carrega essa fonte no OnLoad — sem ela a janela abre e fecha."));

        foreach (string sceneFile in sceneFiles)
            ValidateScene(assetsRoot, sceneFile, known, problems);

        return problems;
    }

    private static void ValidateScene(string assetsRoot, string sceneFile,
        HashSet<string> known, List<Problem> problems)
    {
        string sceneName = Path.GetFileName(sceneFile);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(sceneFile));
        }
        catch (Exception ex)
        {
            problems.Add(new Problem(sceneName, $"não deu pra ler o JSON: {ex.Message}"));
            return;
        }

        if (root is not JsonObject sceneObject)
        {
            problems.Add(new Problem(sceneName, "o arquivo não é um objeto JSON de cena."));
            return;
        }

        bool isUiScreen = sceneObject["UI"]?.GetValue<bool>() ?? false;

        foreach (var entityNode in (sceneObject["Objects"] as JsonArray ?? []).OfType<JsonObject>())
        {
            string entityName = entityNode["Name"]?.GetValue<string>() ?? "(sem nome)";
            string where = $"{sceneName} › {entityName}";

            foreach (var component in (entityNode["Components"] as JsonArray ?? []).OfType<JsonObject>())
            {
                string type = component["Type"]?.GetValue<string>() ?? "";

                ValidateComponentType(type, where, isUiScreen, known, problems);
                ValidateAssetFields(assetsRoot, component, type, where, problems);
                ValidateActions(assetsRoot, component, where, problems);
            }
        }
    }

    private static void ValidateComponentType(string type, string where, bool isUiScreen,
        HashSet<string> known, List<Problem> problems)
    {
        if (type.Length == 0)
        {
            problems.Add(new Problem(where, "componente sem 'Type'."));
            return;
        }

        bool isUiComponent = UiOnlyComponents.Contains(type);

        // Componente de UI numa cena de gameplay: o SceneSerializer não tem leitor pra ele,
        // então some do jogo sem erro nenhum. O contrário também vale — o UIManager só lê Ui*.
        if (isUiComponent && !isUiScreen)
        {
            problems.Add(new Problem(where,
                $"'{type}' só funciona em tela de UI (\"UI\": true). Nesta cena ele é ignorado em silêncio."));
            return;
        }

        if (isUiComponent || known.Contains(type))
            return;

        problems.Add(new Problem(where,
            $"componente '{type}' não é nativo nem um script [SceneScript] do projeto. No jogo ele é ignorado com um aviso no console — a entidade nasce sem esse comportamento."));
    }

    private static void ValidateAssetFields(string assetsRoot, JsonObject component, string type,
        string where, List<Problem> problems)
    {
        foreach (string field in AssetFields)
        {
            if (component[field]?.GetValue<string>() is not { Length: > 0 } path)
                continue;

            if (!AssetExists(assetsRoot, path))
                problems.Add(new Problem(where,
                    $"{type}.{field} aponta pra '{path}', que não existe em Assets/."));
        }
    }

    /// <summary>Ações de EventTrigger ("Actions") e de botão de UI ("OnClick") que carregam
    /// caminho: trocar de cena pra um arquivo que não existe, tocar som que foi apagado.</summary>
    private static void ValidateActions(string assetsRoot, JsonObject component, string where,
        List<Problem> problems)
    {
        foreach (string listName in (string[])["Actions", "OnClick"])
        {
            foreach (var action in (component[listName] as JsonArray ?? []).OfType<JsonObject>())
            {
                string kind = action["Action"]?.GetValue<string>() ?? "";
                if (action["Name"]?.GetValue<string>() is not { Length: > 0 } target)
                    continue;

                bool missing = kind switch
                {
                    "ChangeScene" => !AssetExists(assetsRoot, target),
                    "PlaySound" or "PlayMusic" => !AssetExists(assetsRoot, target),
                    _ => false,
                };

                if (missing)
                    problems.Add(new Problem(where,
                        $"ação {kind} aponta pra '{target}', que não existe em Assets/."));
            }
        }
    }

    /// <summary>Resolve um caminho como o runtime resolve: relativo à raiz de assets, com "/"
    /// virando o separador da plataforma. Caminho absoluto é aceito como está — é o que o
    /// FileAssetSource faz via Path.Combine.</summary>
    private static bool AssetExists(string assetsRoot, string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return File.Exists(Path.Combine(assetsRoot, normalized));
    }
}
