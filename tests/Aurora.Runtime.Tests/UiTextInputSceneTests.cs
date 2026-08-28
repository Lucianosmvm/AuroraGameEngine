using Aurora.Runtime.Assets;
using Aurora.Runtime.Input;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// A costura entre o que o editor grava e o que o runtime lê: uma tela de UI vinda de JSON, com
/// campo de texto ligado a variável. Os outros testes montam o UiTextInput em código e passariam
/// mesmo se o JSON não tivesse a chave.
///
/// <para>O <see cref="AssetManager"/> entra com GL nulo de propósito — <c>LoadText</c> só usa a
/// fonte de assets, e nenhum elemento desta tela carrega textura.</para>
/// </summary>
public sealed class UiTextInputSceneTests : IDisposable
{
    private readonly string _dir;
    private readonly AssetManager _assets;

    public UiTextInputSceneTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aurora-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _assets = new AssetManager(null!, new FileAssetSource(_dir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private string WriteScreen(string json)
    {
        string path = Path.Combine(_dir, "menu.json");
        File.WriteAllText(path, json);
        return "menu.json";
    }

    [Fact]
    public void LeVariableDoJson()
    {
        string path = WriteScreen("""
            {
              "Scene": "menu",
              "UI": true,
              "Objects": [
                {
                  "Name": "CampoNome",
                  "Components": [
                    {
                      "Type": "UiTextInput",
                      "X": 100, "Y": 80, "Width": 200, "Height": 32,
                      "Variable": "NomeJogador",
                      "Placeholder": "Seu nome"
                    }
                  ]
                }
              ]
            }
            """);

        var ui = new UIManager();
        ui.Load(path, _assets);

        var field = ui.Find<UiTextInput>("menu", "CampoNome");

        Assert.NotNull(field);
        Assert.Equal("NomeJogador", field.Variable);
        Assert.Equal("Seu nome", field.Placeholder);
    }

    /// <summary>Tela sem a chave Variable continua válida — é o campo solto de antes.</summary>
    [Fact]
    public void SemVariable_CampoContinuaValido()
    {
        string path = WriteScreen("""
            {
              "Objects": [
                { "Name": "Campo", "Components": [ { "Type": "UiTextInput" } ] }
              ]
            }
            """);

        var ui = new UIManager();
        ui.Load(path, _assets);

        Assert.Equal("", ui.Find<UiTextInput>("menu", "Campo")!.Variable);
    }

    /// <summary>
    /// O caminho completo do caso do usuário, saindo do arquivo: o campo lido do JSON guarda o
    /// que foi digitado, e um UiText mostra isso pelo token {NomeJogador}.
    /// </summary>
    [Fact]
    public void DoJsonAteOTextoNaTela()
    {
        string path = WriteScreen("""
            {
              "Objects": [
                { "Name": "Campo", "Components": [
                    { "Type": "UiTextInput", "Variable": "NomeJogador" } ] },
                { "Name": "Saudacao", "Components": [
                    { "Type": "UiText", "Text": "Olá, {NomeJogador}!" } ] }
              ]
            }
            """);

        var state = new GameState();
        var ui = new UIManager { State = state };
        ui.Load(path, _assets);

        var field = ui.Find<UiTextInput>("menu", "Campo")!;
        field.Text = "Ana";

        var input = new InputManager();
        input.BeginFrame();
        ui.Update(input, null, 1280f, 720f);

        Assert.Equal("Ana", state.GetText("NomeJogador"));

        // O que o UiText desenharia. Draw exige SpriteBatch (e GL), então a checagem é sobre a
        // mesma interpolação que ele usa.
        Assert.Equal("Olá, Ana!", ui.Interpolate("Olá, {NomeJogador}!", state, null, null));
    }
}
