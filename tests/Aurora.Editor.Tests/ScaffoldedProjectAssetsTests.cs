using System.Text.Json;
using System.Text.RegularExpressions;
using Aurora.Editor.Models;

namespace Aurora.Editor.Tests;

/// <summary>
/// "Novo Projeto" tem que sair jogável. O template do Game chama Assets.LoadFont sem checar
/// nada, então um asset citado no código gerado e ausente no disco não dá erro de compilação —
/// dá janela que abre e fecha sozinha no primeiro Play. Foi o que aconteceu quando samples/
/// (única origem da fonte padrão) saiu do repositório: o scaffolder copiava "se achasse", e
/// não achava mais. Estes testes amarram código gerado ↔ arquivo em disco.
/// </summary>
public class ScaffoldedProjectAssetsTests : IDisposable
{
    private readonly string _projectDir = Path.Combine(
        Path.GetTempPath(), "aurora-scaffold-" + Guid.NewGuid().ToString("N"));

    private string Create() => GameProjectScaffolder.Create(_projectDir, "MeuJogo");

    [Fact]
    public void DefaultFont_IsActuallyCopied_NotJustReferenced()
    {
        Create();

        string font = Path.Combine(_projectDir, "Assets", "fonts", "DejaVuSans.ttf");
        Assert.True(File.Exists(font), $"a fonte padrão não foi copiada para {font}");
        Assert.True(new FileInfo(font).Length > 0, "a fonte copiada está vazia");
    }

    [Fact]
    public void EveryAssetTheGeneratedCodeLoads_ExistsOnDisk()
    {
        Create();

        string gameClass = File.ReadAllText(Path.Combine(_projectDir, "MeuJogoGame.cs"));
        var loads = Regex.Matches(gameClass, @"Assets\.Load\w+\(""([^""]+)""");

        Assert.NotEmpty(loads);
        foreach (Match load in loads)
        {
            string relative = load.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar);
            string absolute = Path.Combine(_projectDir, "Assets", relative);
            Assert.True(File.Exists(absolute), $"o código gerado carrega '{load.Groups[1].Value}' e o arquivo não existe");
        }
    }

    [Fact]
    public void UiFontInProjectSettings_PointsToAFileThatExists()
    {
        Create();

        using var settings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(_projectDir, "aurora.project.json")));
        string uiFont = settings.RootElement.GetProperty("uiFont").GetString()!;

        string absolute = Path.Combine(_projectDir, "Assets", uiFont.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(absolute), $"uiFont aponta pra '{uiFont}', que não existe no projeto gerado");
    }

    [Fact]
    public void ExampleScript_LandsInScriptsFolder_WhichTheApkGlobHasToPickUp()
    {
        Create();

        // Se este arquivo mudar de lugar, o glob do AndroidExporter tem que mudar junto
        // (ver AndroidExportSourceGlobTests).
        Assert.True(File.Exists(Path.Combine(_projectDir, "Scripts", "Spin.cs")));
    }

    [Fact]
    public void ReturnsTheStartingScene_AndItExists()
    {
        string scene = Create();

        Assert.True(File.Exists(scene), $"a cena inicial devolvida não existe: {scene}");
    }

    [Fact]
    public void GeneratedGame_DrawsTheDialogue_SoShowMessageIsVisible()
    {
        // ShowMessage é uma ação oferecida no editor desde o primeiro dia. Sem Dialogue.Draw no
        // OnRenderUI ela registra o texto e NADA aparece na tela: o evento parece ignorado, e não
        // existe erro nenhum pra investigar. Mesma família do bug da fonte — o template
        // prometendo algo que não liga.
        Create();

        string gameClass = File.ReadAllText(Path.Combine(_projectDir, "MeuJogoGame.cs"));

        Assert.Contains("Dialogue.Draw(", gameClass);
    }

    [Fact]
    public void GeneratedGame_DrawsTheDialogueAfterTheUi()
    {
        // A caixa de fala tem que ficar POR CIMA do HUD, não atrás dele.
        Create();

        string gameClass = File.ReadAllText(Path.Combine(_projectDir, "MeuJogoGame.cs"));

        Assert.True(gameClass.IndexOf("UI.Draw(", StringComparison.Ordinal)
                    < gameClass.IndexOf("Dialogue.Draw(", StringComparison.Ordinal),
            "Dialogue.Draw precisa vir depois de UI.Draw.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { /* melhor esforço */ }
    }
}
