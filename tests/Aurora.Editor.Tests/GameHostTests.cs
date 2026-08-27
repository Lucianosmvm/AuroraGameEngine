using Aurora.Editor.Models;

namespace Aurora.Editor.Tests;

/// <summary>
/// Um projeto de jogo de mentira, compilado de verdade numa pasta temporária. Compilar é lento
/// (segundos), então a pasta é montada uma vez e compartilhada pela classe de teste — só o teste
/// de hot-swap recompila, porque é justamente disso que ele trata.
/// </summary>
public sealed class GameProjectFixture : IDisposable
{
    public string Directory { get; }
    public string ProjectPath { get; }
    public string SourcePath { get; }

    public GameProjectFixture()
    {
        Directory = Path.Combine(Path.GetTempPath(), "aurora-gamehost-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);

        ProjectPath = Path.Combine(Directory, "FakeGame.csproj");
        SourcePath = Path.Combine(Directory, "MyGame.cs");

        // Referência ao runtime pelo .dll que já está na saída deste teste — assim o projeto
        // temporário não precisa saber onde o repositório está.
        string runtimeDll = Path.Combine(AppContext.BaseDirectory, "Aurora.Runtime.dll");
        Assert.True(File.Exists(runtimeDll), $"Aurora.Runtime.dll não está na saída do teste: {runtimeDll}");

        File.WriteAllText(ProjectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <AssemblyName>FakeGame</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Aurora.Runtime">
                  <HintPath>{runtimeDll}</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);

        WriteSource("v1");
    }

    /// <summary>Reescreve o jogo com uma marca de versão — é como o teste distingue o código
    /// velho do novo depois de recarregar.</summary>
    public void WriteSource(string version) => File.WriteAllText(SourcePath, $$"""
        using Aurora.Runtime;

        public sealed class MyGame : Game
        {
            public const string Version = "{{version}}";
            protected override void OnLoad() { }
        }
        """);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária presa por antivírus/indexador não é falha de teste.
        }
    }
}

/// <summary>
/// O ciclo que sustenta o play-in-editor: compilar, carregar, descarregar e recarregar o código
/// novo sem reiniciar o editor. O teste que mais importa aqui é o de descarga — se o contexto
/// não for coletado, o próximo Play roda o código velho em silêncio, que é o pior modo de falhar.
/// </summary>
public sealed class GameHostTests : IClassFixture<GameProjectFixture>
{
    private readonly GameProjectFixture _fixture;

    public GameHostTests(GameProjectFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Build_DevolveOCaminhoDoAssembly()
    {
        var (result, path) = await GameHost.BuildAsync(_fixture.ProjectPath);

        Assert.True(result.Ok, result.Message + Environment.NewLine + result.Detail);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal("FakeGame.dll", Path.GetFileName(path));
    }

    [Fact]
    public async Task Build_ProjetoInexistente_FalhaComMensagem()
    {
        var (result, path) = await GameHost.BuildAsync(
            Path.Combine(_fixture.Directory, "NaoExiste.csproj"));

        Assert.False(result.Ok);
        Assert.Null(path);
        Assert.Contains("não encontrado", result.Message);
    }

    [Fact]
    public async Task Load_EncontraASubclasseDeGame()
    {
        var (build, path) = await GameHost.BuildAsync(_fixture.ProjectPath);
        Assert.True(build.Ok, build.Detail);

        using var host = new GameHost();
        var result = host.Load(path!);

        Assert.True(result.Ok, result.Message + Environment.NewLine + result.Detail);
        Assert.True(host.IsLoaded);
        Assert.Contains("MyGame", result.Message);

        host.Stop(shutdownGame: false);
    }

    /// <summary>
    /// Carregar de bytes em vez de caminho é o que mantém o .dll gravável. Com
    /// LoadFromAssemblyPath o Windows tranca o arquivo e o build seguinte do usuário morre com
    /// "arquivo em uso" — falha que aparece longe daqui e não explica a causa.
    /// </summary>
    [Fact]
    public async Task Load_NaoTrancaOArquivoDoAssembly()
    {
        var (build, path) = await GameHost.BuildAsync(_fixture.ProjectPath);
        Assert.True(build.Ok, build.Detail);

        using var host = new GameHost();
        Assert.True(host.Load(path!).Ok);

        // Abrir pra escrita com FileShare.None é o que o compilador faz ao regravar a saída.
        using (var stream = new FileStream(path!, FileMode.Open, FileAccess.Write, FileShare.None))
            Assert.True(stream.CanWrite);

        host.Stop(shutdownGame: false);
    }

    [Fact]
    public async Task Stop_ColetaOContexto()
    {
        var (build, path) = await GameHost.BuildAsync(_fixture.ProjectPath);
        Assert.True(build.Ok, build.Detail);

        using var host = new GameHost();
        Assert.True(host.Load(path!).Ok);

        // shutdownGame: false porque não há contexto de GL neste teste — Shutdown liberaria
        // recursos de GL que nunca foram criados.
        host.Stop(shutdownGame: false);

        Assert.False(host.IsLoaded);
        Assert.True(host.LastContextCollected,
            "O contexto do jogo não foi coletado: algo ainda segura um objeto do assembly do "
            + "jogo, e um novo Play rodaria o código antigo.");
    }

    /// <summary>O ciclo inteiro do botão Play depois de uma edição de script.</summary>
    [Fact]
    public async Task RecarregaCodigoRecompilado()
    {
        _fixture.WriteSource("v1");
        var (build1, path1) = await GameHost.BuildAsync(_fixture.ProjectPath);
        Assert.True(build1.Ok, build1.Detail);

        using var host = new GameHost();
        Assert.True(host.Load(path1!).Ok);
        Assert.Equal("v1", ReadVersion(host));

        host.Stop(shutdownGame: false);

        _fixture.WriteSource("v2");
        var (build2, path2) = await GameHost.BuildAsync(_fixture.ProjectPath);
        Assert.True(build2.Ok, build2.Detail);

        Assert.True(host.Load(path2!).Ok);
        Assert.Equal("v2", ReadVersion(host));

        host.Stop(shutdownGame: false);
    }

    [Fact]
    public async Task Load_DuasVezesSemParar_Recusa()
    {
        var (build, path) = await GameHost.BuildAsync(_fixture.ProjectPath);
        Assert.True(build.Ok, build.Detail);

        using var host = new GameHost();
        Assert.True(host.Load(path!).Ok);

        var second = host.Load(path!);
        Assert.False(second.Ok);
        Assert.Contains("pare antes", second.Message);

        host.Stop(shutdownGame: false);
    }

    /// <summary>Um .dll sem subclasse de Game é engano comum (apontar pro projeto errado) —
    /// tem que dizer isso, não estourar exceção crua.</summary>
    [Fact]
    public void Load_AssemblySemGame_FalhaComMensagemClara()
    {
        using var host = new GameHost();

        // O próprio Aurora.Runtime.dll: existe, carrega, e não tem subclasse concreta de Game.
        var result = host.Load(Path.Combine(AppContext.BaseDirectory, "Aurora.Runtime.dll"));

        Assert.False(result.Ok);
        Assert.Contains("Nenhuma subclasse de Game", result.Message);
        Assert.False(host.IsLoaded);
    }

    /// <summary>Lê a constante Version sem guardar nada de tipo do jogo — segurar um objeto
    /// desses aqui prenderia o contexto e faria o teste de descarga passar por engano.</summary>
    private static string ReadVersion(GameHost host)
    {
        var type = host.Game!.GetType();
        return (string)type.GetField("Version")!.GetRawConstantValue()!;
    }
}
