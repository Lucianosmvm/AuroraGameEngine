using Aurora.Editor.Models;

namespace Aurora.Editor.Tests;

/// <summary>
/// O resumo que a status bar mostra quando o processo do JOGO morre. Um crash de runtime não
/// se parece com um erro de build: sai como "Unhandled exception." e não contém "error", então
/// o extrator de erro de build devolvia só o texto de fallback e o usuário via a janela abrir e
/// fechar sem explicação nenhuma.
/// </summary>
public class GameCrashSummaryTests
{
    private const string RealCrash = """
        Unhandled exception. System.IO.FileNotFoundException: Asset não encontrado: fonts/DejaVuSans.ttf (procurado em C:/Jogo/Assets/fonts/DejaVuSans.ttf)
        File name: 'C:/Jogo/Assets/fonts/DejaVuSans.ttf'
           at Aurora.Runtime.Assets.FileAssetSource.Open(String path)
           at MeuJogo.MeuJogoGame.OnLoad()
        """;

    [Fact]
    public void UnhandledExceptionOnOneLine_KeepsTypeAndMessage()
    {
        string summary = GameScriptDiscovery.FirstCrashLine("", RealCrash);

        Assert.Equal(
            "System.IO.FileNotFoundException: Asset não encontrado: fonts/DejaVuSans.ttf (procurado em C:/Jogo/Assets/fonts/DejaVuSans.ttf)",
            summary);
    }

    [Fact]
    public void UnhandledExceptionSplitAcrossLines_UsesNextLine()
    {
        string summary = GameScriptDiscovery.FirstCrashLine(
            "", "Unhandled exception.\nSystem.InvalidOperationException: cena sem câmera\n   at Foo.Bar()");

        Assert.Equal("System.InvalidOperationException: cena sem câmera", summary);
    }

    [Fact]
    public void FallsBackToStdout_WhenStderrIsEmpty()
    {
        string summary = GameScriptDiscovery.FirstCrashLine(
            "Unhandled exception. System.Exception: veio pelo stdout", "   \n  ");

        Assert.Equal("System.Exception: veio pelo stdout", summary);
    }

    [Fact]
    public void WithoutUnhandledMarker_FallsBackToFirstLine()
    {
        string summary = GameScriptDiscovery.FirstCrashLine("", "Segmentation fault (core dumped)");

        Assert.Equal("Segmentation fault (core dumped)", summary);
    }

    [Fact]
    public void NoOutputAtAll_StillExplainsItself()
    {
        string summary = GameScriptDiscovery.FirstCrashLine("", "");

        Assert.Contains("sem saída", summary);
    }

    /// <summary>Erro de BUILD continua indo pelo extrator antigo — este teste trava os dois
    /// caminhos separados, pra ninguém unificar um no outro e regredir a mensagem.</summary>
    [Fact]
    public void BuildErrorExtractor_StillPicksTheErrorLine()
    {
        string summary = GameScriptDiscovery.FirstErrorLine(
            "MeuJogo.cs(12,5): error CS0103: O nome 'Foo' não existe", "");

        Assert.Contains("error CS0103", summary);
    }
}
