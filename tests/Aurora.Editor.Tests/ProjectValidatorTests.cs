using Aurora.Editor.Models;

namespace Aurora.Editor.Tests;

/// <summary>
/// Regras do "Validar Projeto". Cada uma existe por um sintoma que o compilador não pega:
/// textura apagada = jogo fecha sozinho no OnLoad; componente desconhecido = entidade nasce
/// sem o comportamento e só o console reclama; ChangeScene pra arquivo que não existe = botão
/// que não faz nada.
/// </summary>
public class ProjectValidatorTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "aurora-val-" + Guid.NewGuid().ToString("N"));

    private static readonly string[] Known = ["Transform", "SpriteRenderer", "Collider", "EventTrigger", "Tilemap"];

    public ProjectValidatorTests()
    {
        Directory.CreateDirectory(Path.Combine(_assets, "scenes"));
        Directory.CreateDirectory(Path.Combine(_assets, "sprites"));
        Directory.CreateDirectory(Path.Combine(_assets, "fonts"));
        File.WriteAllText(Path.Combine(_assets, "sprites", "chao.png"), "png");
        File.WriteAllText(Path.Combine(_assets, "fonts", "DejaVuSans.ttf"), "ttf");
    }

    private string WriteScene(string name, string json)
    {
        string path = Path.Combine(_assets, "scenes", name);
        File.WriteAllText(path, json);
        return path;
    }

    private IReadOnlyList<ProjectValidator.Problem> Validate(string sceneJson, string? uiFont = null)
    {
        string scene = WriteScene("fase.json", sceneJson);
        return ProjectValidator.Validate(_assets, [scene], Known, uiFont);
    }

    [Fact]
    public void CleanScene_ReportsNothing()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{
                "Name": "Chao",
                "Components": [
                  { "Type": "Transform", "X": 0, "Y": 0 },
                  { "Type": "SpriteRenderer", "Texture": "sprites/chao.png" }
                ]
              }]
            }
            """, uiFont: "fonts/DejaVuSans.ttf");

        Assert.Empty(problems);
    }

    [Fact]
    public void MissingTexture_IsReported_WithSceneAndEntity()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{
                "Name": "Chao",
                "Components": [{ "Type": "SpriteRenderer", "Texture": "sprites/sumiu.png" }]
              }]
            }
            """);

        var problem = Assert.Single(problems);
        Assert.Equal("fase.json › Chao", problem.Where);
        Assert.Contains("sprites/sumiu.png", problem.Message);
    }

    [Fact]
    public void UnknownComponent_IsReported_BecauseTheGameSkipsItSilently()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{ "Name": "Inimigo", "Components": [{ "Type": "Perseguir" }] }]
            }
            """);

        Assert.Contains(problems, p => p.Message.Contains("Perseguir"));
    }

    [Fact]
    public void CustomScriptPassedIn_IsNotAFalsePositive()
    {
        string scene = WriteScene("fase.json", """
            {
              "Scene": "fase",
              "Objects": [{ "Name": "Inimigo", "Components": [{ "Type": "Perseguir" }] }]
            }
            """);

        var problems = ProjectValidator.Validate(_assets, [scene], [.. Known, "Perseguir"], null);

        Assert.Empty(problems);
    }

    [Fact]
    public void UiComponentInAGameplayScene_IsReported()
    {
        // Cena sem "UI": true: o SceneSerializer não tem leitor de UiButton, então o botão
        // simplesmente não existe no jogo.
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{ "Name": "Botao", "Components": [{ "Type": "UiButton", "Text": "Jogar" }] }]
            }
            """);

        Assert.Contains(problems, p => p.Message.Contains("só funciona em tela de UI"));
    }

    [Fact]
    public void UiComponentInAUiScreen_IsFine()
    {
        var problems = Validate("""
            {
              "Scene": "MainMenu",
              "UI": true,
              "Objects": [{ "Name": "Botao", "Components": [{ "Type": "UiButton", "Text": "Jogar" }] }]
            }
            """);

        Assert.Empty(problems);
    }

    [Fact]
    public void ChangeSceneToAMissingFile_IsReported()
    {
        var problems = Validate("""
            {
              "Scene": "MainMenu",
              "UI": true,
              "Objects": [{
                "Name": "Botao",
                "Components": [{
                  "Type": "UiButton",
                  "OnClick": [{ "Action": "ChangeScene", "Name": "scenes/fase99.json" }]
                }]
              }]
            }
            """);

        Assert.Contains(problems, p => p.Message.Contains("fase99.json"));
    }

    [Fact]
    public void ChangeSceneToAnExistingFile_IsFine()
    {
        WriteScene("fase2.json", """{ "Scene": "fase2", "Objects": [] }""");

        var problems = Validate("""
            {
              "Scene": "MainMenu",
              "UI": true,
              "Objects": [{
                "Name": "Botao",
                "Components": [{
                  "Type": "UiButton",
                  "OnClick": [{ "Action": "ChangeScene", "Name": "scenes/fase2.json" }]
                }]
              }]
            }
            """);

        Assert.Empty(problems);
    }

    [Fact]
    public void MissingSound_InAnEventTriggerAction_IsReported()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{
                "Name": "Porta",
                "Components": [{
                  "Type": "EventTrigger",
                  "Actions": [{ "Action": "PlaySound", "Name": "sounds/porta.wav" }]
                }]
              }]
            }
            """);

        Assert.Contains(problems, p => p.Message.Contains("porta.wav"));
    }

    [Fact]
    public void ActionWithoutAPath_IsNotChecked()
    {
        // SetSwitch/ShowText não carregam caminho de arquivo — "Name" ali é outra coisa.
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{
                "Name": "Porta",
                "Components": [{
                  "Type": "EventTrigger",
                  "Actions": [{ "Action": "SetSwitch", "Name": "porta_aberta", "On": true }]
                }]
              }]
            }
            """);

        Assert.Empty(problems);
    }

    [Fact]
    public void MissingUiFont_IsReported_BecauseItIsTheClassicSilentCrash()
    {
        string scene = WriteScene("fase.json", """{ "Scene": "fase", "Objects": [] }""");

        var problems = ProjectValidator.Validate(_assets, [scene], Known, "fonts/NaoExiste.ttf");

        var problem = Assert.Single(problems);
        Assert.Equal("aurora.project.json", problem.Where);
        Assert.Contains("abre e fecha", problem.Message);
    }

    [Fact]
    public void BrokenJson_IsReportedInsteadOfThrowing()
    {
        string scene = WriteScene("fase.json", "{ isto não é json }");

        var problems = ProjectValidator.Validate(_assets, [scene], Known, null);

        Assert.Contains(problems, p => p.Where == "fase.json");
    }

    [Fact]
    public void ComponentWithoutType_IsReported()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{ "Name": "Solto", "Components": [{ "X": 1 }] }]
            }
            """);

        Assert.Contains(problems, p => p.Message.Contains("sem 'Type'"));
    }

    [Fact]
    public void EveryProblemInEveryEntity_IsListed_NotJustTheFirst()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [
                { "Name": "A", "Components": [{ "Type": "SpriteRenderer", "Texture": "sprites/a.png" }] },
                { "Name": "B", "Components": [{ "Type": "SpriteRenderer", "Texture": "sprites/b.png" }] }
              ]
            }
            """);

        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void ParentPointingToAnEntityThatIsNotInTheScene_IsReported()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{
                "Name": "Arma",
                "Components": [{ "Type": "Transform", "X": 0, "Y": 0, "Parent": "Player" }]
              }]
            }
            """);

        Assert.Contains(problems, p => p.Message.Contains("Parent aponta pra 'Player'"));
    }

    [Fact]
    public void ParentPointingToAnEntityInTheScene_IsFine()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [
                { "Name": "Player", "Components": [{ "Type": "Transform", "X": 0, "Y": 0 }] },
                { "Name": "Arma", "Components": [{ "Type": "Transform", "X": 5, "Y": 0, "Parent": "Player" }] }
              ]
            }
            """);

        Assert.Empty(problems);
    }

    [Fact]
    public void EntityParentedToItself_IsReported()
    {
        var problems = Validate("""
            {
              "Scene": "fase",
              "Objects": [{
                "Name": "Arma",
                "Components": [{ "Type": "Transform", "X": 0, "Y": 0, "Parent": "Arma" }]
              }]
            }
            """);

        Assert.Contains(problems, p => p.Message.Contains("própria entidade"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_assets, recursive: true); } catch { /* melhor esforço */ }
    }
}
