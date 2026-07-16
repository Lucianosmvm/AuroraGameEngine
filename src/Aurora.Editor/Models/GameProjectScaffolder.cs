using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Aurora.Editor.Models;

/// <summary>
/// Gera um projeto de jogo executável mínimo (.csproj + Program.cs + classe Game + cena
/// inicial) a partir do editor, para que "New Project" produza algo jogável sem exigir
/// que o usuário escreva esses arquivos na mão.
/// </summary>
public static class GameProjectScaffolder
{
    /// <summary>
    /// Cria <paramref name="projectDir"/> com csproj, Program.cs, classe de jogo, cena
    /// inicial vazia (Assets/scenes/main.json) e aurora.project.json já apontando pro
    /// csproj gerado. Retorna o caminho da cena criada, pronta pra abrir no editor.
    /// </summary>
    public static string Create(string projectDir, string projectName)
    {
        if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
            throw new InvalidOperationException($"'{projectDir}' já existe e não está vazia.");

        string runtimeCsproj = FindRuntimeCsproj()
            ?? throw new InvalidOperationException(
                "Não encontrei Aurora.Runtime.csproj automaticamente (rode o editor a partir do repositório). " +
                "Crie o projeto manualmente e aponte PROJETO pro executável.");

        string identifier = ToIdentifier(projectName);

        Directory.CreateDirectory(projectDir);
        string scenesDir = Path.Combine(projectDir, "Assets", "scenes");
        Directory.CreateDirectory(scenesDir);
        string spritesDir = Path.Combine(projectDir, "Assets", "sprites");
        Directory.CreateDirectory(spritesDir);
        string fontsDir = Path.Combine(projectDir, "Assets", "fonts");
        Directory.CreateDirectory(fontsDir);
        string scriptsDir = Path.Combine(projectDir, "Scripts");
        Directory.CreateDirectory(scriptsDir);

        string relativeRuntimePath = Path.GetRelativePath(projectDir, runtimeCsproj);
        File.WriteAllText(Path.Combine(projectDir, $"{projectName}.csproj"), BuildCsproj(relativeRuntimePath));
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), BuildProgram(identifier));
        File.WriteAllText(Path.Combine(projectDir, $"{identifier}Game.cs"), BuildGameClass(identifier));
        File.WriteAllText(Path.Combine(scriptsDir, "Spin.cs"), BuildExampleScript(identifier));
        File.WriteAllBytes(Path.Combine(spritesDir, "placeholder.png"), Convert.FromBase64String(PlaceholderPngBase64));

        string? fontSource = FindDefaultFont();
        if (fontSource is not null)
            File.Copy(fontSource, Path.Combine(fontsDir, "DejaVuSans.ttf"), overwrite: true);

        // Tela de UI carregada e desenhada automaticamente pelo template do Game (ver
        // BuildGameClass) — "Novo Projeto" já sai com um menu clicável funcionando no Play,
        // sem precisar escrever UI.Load/UI.Draw na mão.
        string menuScenePath = Path.Combine(scenesDir, "MainMenu.json");
        var menuRoot = new JsonObject
        {
            ["Scene"] = "MainMenu",
            ["UI"] = true,
            ["Objects"] = new JsonArray(
                new JsonObject
                {
                    ["Name"] = "PlayButton",
                    ["Components"] = new JsonArray(
                        new JsonObject
                        {
                            ["Type"] = "UiButton",
                            ["X"] = 0f,
                            ["Y"] = 0f,
                            ["AnchorX"] = "Center",
                            ["AnchorY"] = "Center",
                            ["Width"] = 200f,
                            ["Height"] = 48f,
                            ["Text"] = "Jogar",
                            ["OnClick"] = new JsonArray(
                                // HideUI primeiro: sem isso o botão do menu fica desenhado por
                                // cima pra sempre (UiScreen persiste entre ChangeScene).
                                new JsonObject { ["Action"] = "HideUI", ["Name"] = "MainMenu" },
                                new JsonObject { ["Action"] = "ChangeScene", ["Name"] = "scenes/main.json" }),
                        }),
                }),
        };
        File.WriteAllText(menuScenePath, menuRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // Cena não vem vazia: uma entidade já com o sprite placeholder + o script de exemplo
        // (Spin), pra Play mostrar algo visível E rodando de cara — inclusive provando que
        // scripting funciona sem precisar registrar nada na mão (ver Spin.cs).
        string scenePath = Path.Combine(scenesDir, "main.json");
        var sceneRoot = new JsonObject
        {
            ["Scene"] = "main",
            ["Objects"] = new JsonArray(
                new JsonObject
                {
                    ["Name"] = "Placeholder",
                    ["Components"] = new JsonArray(
                        new JsonObject { ["Type"] = "Transform", ["X"] = 0f, ["Y"] = 0f },
                        new JsonObject { ["Type"] = "SpriteRenderer", ["Texture"] = "sprites/placeholder.png" },
                        new JsonObject { ["Type"] = "Spin", ["Speed"] = 90f }),
                }),
        };
        File.WriteAllText(scenePath, sceneRoot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var settings = new ProjectSettings { GameProject = Path.Combine(projectDir, $"{projectName}.csproj") };
        File.WriteAllText(Path.Combine(projectDir, "aurora.project.json"),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        return scenePath;
    }

    /// <summary>Quadriculado magenta/roxo 32x32 — mesma vibe do placeholder que o editor já desenha
    /// pra sprites sem textura, só que como arquivo real (o runtime não desenha placeholder, precisa
    /// de textura de verdade pra aparecer algo).</summary>
    private const string PlaceholderPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABGSURBVFhH7c6hDQAwDANBD9EhMkT3VyfpGg03swJKHjwxsE53nzepVo2SH6b5YRoAAAAAAAAAQD6kOSgNAAAAAAAAAPAd0C92mHnKU5Y6AAAAAElFTkSuQmCC";

    /// <summary>Sobe a partir da pasta do executável do editor até achar src/Aurora.Runtime/Aurora.Runtime.csproj.</summary>
    private static string? FindRuntimeCsproj()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "src", "Aurora.Runtime", "Aurora.Runtime.csproj");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>Sobe a partir da pasta do executável até achar a fonte padrão usada pelos samples,
    /// pra todo projeto novo já sair com texto de UI (botão do menu, HUD) desenhando de cara.</summary>
    private static string? FindDefaultFont()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "samples", "Aurora.Sandbox.Core", "Assets", "fonts", "DejaVuSans.ttf");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static string ToIdentifier(string name)
    {
        string cleaned = Regex.Replace(name, "[^A-Za-z0-9_]", "");
        if (cleaned.Length == 0)
            cleaned = "MeuJogo";
        if (char.IsDigit(cleaned[0]))
            cleaned = "_" + cleaned;
        return cleaned;
    }

    private static string BuildCsproj(string relativeRuntimePath) => $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>

          <ItemGroup>
            <ProjectReference Include="{relativeRuntimePath}" />
          </ItemGroup>

          <ItemGroup>
            <None Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
          </ItemGroup>

        </Project>
        """;

    private static string BuildProgram(string identifier) => $"""
        using {identifier};

        using var game = new {identifier}Game();
        game.ParseArgs(args);
        game.Run("{identifier}");
        """;

    private static string BuildGameClass(string identifier) => $$"""
        using Aurora.Runtime;
        using Aurora.Runtime.Graphics;

        namespace {{identifier}};

        public sealed class {{identifier}}Game : Game
        {
            private Font _font = null!;

            protected override void OnLoad()
            {
                _font = Assets.LoadFont("fonts/DejaVuSans.ttf", 22f);
                UI.Load("scenes/MainMenu.json", Assets);
                LoadScene(BootScene ?? "scenes/main.json");
            }

            protected override void OnRenderUI(float dt)
            {
                UI.Draw(SpriteBatch, _font, State, Inventory, Quests, View.FramebufferSize.X, View.FramebufferSize.Y);
            }
        }
        """;

    private static string BuildExampleScript(string identifier) => $$"""
        using Aurora.Runtime.Ecs;
        using Aurora.Runtime.Ecs.Components;
        using Aurora.Runtime.Scenes;

        namespace {{identifier}};

        // Exemplo de script custom: marque com [SceneScript] e ele já funciona na cena,
        // sem precisar registrar nada em OnLoad() nem escrever leitura/escrita de JSON.
        // Campos públicos float/int/bool/string (ex.: Speed abaixo) viram campos editáveis
        // no JSON da cena automaticamente, pelo próprio nome.
        [SceneScript]
        public sealed class Spin : Behavior
        {
            public float Speed = 90f;

            public override void Update(float deltaTime)
            {
                var transform = Get<Transform>();
                if (transform is not null)
                    transform.Rotation += Speed * deltaTime;
            }
        }
        """;
}
