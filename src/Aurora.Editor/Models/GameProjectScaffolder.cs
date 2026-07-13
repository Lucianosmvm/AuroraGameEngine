using System.Text.Json;
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

        string relativeRuntimePath = Path.GetRelativePath(projectDir, runtimeCsproj);
        File.WriteAllText(Path.Combine(projectDir, $"{projectName}.csproj"), BuildCsproj(relativeRuntimePath));
        File.WriteAllText(Path.Combine(projectDir, "Program.cs"), BuildProgram(identifier));
        File.WriteAllText(Path.Combine(projectDir, $"{identifier}Game.cs"), BuildGameClass(identifier));

        string scenePath = Path.Combine(scenesDir, "main.json");
        SceneDocument.New(scenePath);

        var settings = new ProjectSettings { GameProject = Path.Combine(projectDir, $"{projectName}.csproj") };
        File.WriteAllText(Path.Combine(projectDir, "aurora.project.json"),
            JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

        return scenePath;
    }

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

        namespace {{identifier}};

        public sealed class {{identifier}}Game : Game
        {
            protected override void OnLoad()
            {
                LoadScene(BootScene ?? "scenes/main.json");
            }
        }
        """;
}
