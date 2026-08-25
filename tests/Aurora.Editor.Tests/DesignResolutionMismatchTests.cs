using Aurora.Editor.ViewModels;

namespace Aurora.Editor.Tests;

/// <summary>
/// A moldura do editor sai do aurora.project.json; a tela do jogo sai do DesignResolution escrito
/// no código. Divergirem é o único jeito do preview de menu mentir — e o sintoma em jogo (elemento
/// ancorado em Center fora do lugar) não aponta pra causa. Estes testes prendem a conferência.
/// </summary>
public class DesignResolutionMismatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "aurora-design-" + Guid.NewGuid().ToString("N"));
    private readonly MainViewModel _vm = new();
    private readonly string _gameDir;

    public DesignResolutionMismatchTests()
    {
        Directory.CreateDirectory(_dir);
        _gameDir = Path.Combine(_dir, "jogo");
        Directory.CreateDirectory(_gameDir);

        string scene = Path.Combine(_dir, "menu.json");
        File.WriteAllText(scene, """{ "Scene": "menu", "Objects": [] }""");
        _vm.OpenScene(scene);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void WriteGameCode(string designResolutionLine)
    {
        File.WriteAllText(Path.Combine(_gameDir, "MeuJogo.cs"), $$"""
            public sealed class MeuJogo : Game
            {
                public MeuJogo()
                {
                    {{designResolutionLine}}
                }
            }
            """);
        _vm.GameProjectPath = _gameDir;
    }

    [Fact]
    public void AvisaQuandoOCodigoUsaOutraResolucao()
    {
        WriteGameCode("DesignResolution = new Vector2D<int>(1280, 720);");
        _vm.DesignWidth = 720;
        _vm.DesignHeight = 1280;

        Assert.True(_vm.HasDesignResolutionMismatch);
        Assert.Contains("1280x720", _vm.DesignResolutionMismatch);
        Assert.Contains("720x1280", _vm.DesignResolutionMismatch);
    }

    [Fact]
    public void CaladoQuandoOsDoisBatem()
    {
        WriteGameCode("DesignResolution = new Vector2D<int>(1280, 720);");
        _vm.DesignWidth = 1280;
        _vm.DesignHeight = 720;

        Assert.False(_vm.HasDesignResolutionMismatch);
    }

    [Fact]
    public void EspacamentoNoCodigoNaoEngana()
    {
        WriteGameCode("DesignResolution   =  new  Vector2D<int> ( 1920 , 1080 ) ;");
        _vm.DesignWidth = 1280;
        _vm.DesignHeight = 720;

        Assert.Contains("1920x1080", _vm.DesignResolutionMismatch);
    }

    [Fact]
    public void SemProjetoApontadoNaoInventaAviso()
    {
        // "Não sei" não é "está errado": sem código pra ler, nada a dizer.
        _vm.DesignWidth = 720;
        _vm.DesignHeight = 1280;

        Assert.False(_vm.HasDesignResolutionMismatch);
    }

    [Fact]
    public void ResolucaoCalculadaEmRuntimeNaoVirouFalsoPositivo()
    {
        WriteGameCode("DesignResolution = EscolherResolucao();");
        _vm.DesignWidth = 720;
        _vm.DesignHeight = 1280;

        Assert.False(_vm.HasDesignResolutionMismatch);
    }
}
