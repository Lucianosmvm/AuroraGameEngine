using Aurora.Editor.Models;

namespace Aurora.Editor.Tests;

/// <summary>
/// Trava o glob de código-fonte do projeto Android gerado. O APK compila os .cs do jogo por
/// glob (não por ProjectReference — NETSDK1150), e um glob só de raiz deixava Scripts/ de fora:
/// o APK saía sem os comportamentos e ninguém era avisado, porque componente não registrado
/// vira só uma linha no logcat (ver SceneSerializer.CreateEntity).
/// </summary>
public class AndroidExportSourceGlobTests : IDisposable
{
    private static readonly char Sep = Path.DirectorySeparatorChar;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "aurora-droid-" + Guid.NewGuid().ToString("N"));
    private readonly string _gameDir;
    private readonly string _androidDir;

    public AndroidExportSourceGlobTests()
    {
        // O exportador sobe da pasta do jogo procurando src/Aurora.Runtime/Aurora.Runtime.csproj.
        Directory.CreateDirectory(Path.Combine(_root, "src", "Aurora.Runtime"));
        File.WriteAllText(Path.Combine(_root, "src", "Aurora.Runtime", "Aurora.Runtime.csproj"), "<Project />");

        _gameDir = Path.Combine(_root, "MeuJogo");
        _androidDir = Path.Combine(_root, "MeuJogo.Android");
        Directory.CreateDirectory(Path.Combine(_gameDir, "Scripts"));

        File.WriteAllText(Path.Combine(_gameDir, "MeuJogo.csproj"), "<Project />");

        // Mesma forma que o GameProjectScaffolder gera: entry point sem namespace na raiz,
        // classe do jogo na raiz, comportamento em Scripts/.
        File.WriteAllText(Path.Combine(_gameDir, "Program.cs"), """
            using MeuJogo;

            using var game = new MeuJogoGame();
            game.Run("MeuJogo");
            """);
        File.WriteAllText(Path.Combine(_gameDir, "MeuJogoGame.cs"), """
            namespace MeuJogo;

            public sealed class MeuJogoGame : Game { }
            """);
        File.WriteAllText(Path.Combine(_gameDir, "Scripts", "Spin.cs"), """
            namespace MeuJogo;

            public sealed class Spin : Behavior { }
            """);
    }

    private string ExportedCsproj()
    {
        var result = AndroidExporter.Export(
            Path.Combine(_gameDir, "MeuJogo.csproj"), _androidDir, "com.teste.meujogo", "MeuJogo");
        return File.ReadAllText(result.CsprojPath);
    }

    [Fact]
    public void CompileGlob_IsRecursive_SoScriptsFolderEntersTheApk()
    {
        string csproj = ExportedCsproj();

        Assert.Contains(Path.Combine("MeuJogo", "**", "*.cs"), csproj);
        // O glob antigo (só a raiz) não pode voltar: ele fechava aspas logo depois do *.cs.
        Assert.DoesNotContain(Path.Combine("MeuJogo", "*.cs") + "\"", csproj);
    }

    [Fact]
    public void CompileGlob_LinkKeepsSubfolder_SoScriptsWithTheSameNameDontCollide()
    {
        Assert.Contains("%(RecursiveDir)%(Filename)%(Extension)", ExportedCsproj());
    }

    [Fact]
    public void CompileGlob_ExcludesGameBinAndObj()
    {
        // Fora da pasta do projeto Android o DefaultItemExcludes não alcança: sem excluir à
        // mão, o AssemblyInfo gerado em obj/ entra no glob e duplica atributo (CS0579).
        string csproj = ExportedCsproj();

        Assert.Contains("MeuJogo" + Sep + "bin" + Sep + "**", csproj);
        Assert.Contains("MeuJogo" + Sep + "obj" + Sep + "**", csproj);
    }

    [Fact]
    public void ProgramCs_StaysOut_BecauseAndroidEntryPointIsMainActivity()
    {
        string csproj = ExportedCsproj();

        int excludeAt = csproj.IndexOf("Exclude=", StringComparison.Ordinal);
        Assert.True(excludeAt > 0, "o item Compile precisa ter Exclude");

        string excludeAttribute = csproj[excludeAt..csproj.IndexOf("/>", excludeAt, StringComparison.Ordinal)];
        Assert.Contains("Program.cs", excludeAttribute);
    }

    [Fact]
    public void AssetsStillGoIn_WithFolderStructurePreserved()
    {
        string csproj = ExportedCsproj();

        Assert.Contains("AndroidAsset", csproj);
        Assert.Contains(Path.Combine("MeuJogo", "Assets", "**"), csproj);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* melhor esforço */ }
    }
}
