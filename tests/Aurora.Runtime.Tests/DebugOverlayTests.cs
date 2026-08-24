using Aurora.Runtime;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Overlay de diagnóstico (<c>--debug</c>). O desenho em si precisa de GL e não roda aqui;
/// o que dá pra travar sem janela é o que costuma sair errado: a flag ser reconhecida e o
/// FPS ser média, não contagem crua.
/// </summary>
public class DebugOverlayTests
{
    /// <summary>Game é abstrato e só toca GL dentro do Run — construir pra ler ParseArgs é seguro.</summary>
    private sealed class HeadlessGame : Game
    {
        protected override void OnLoad() { }

        /// <summary>BootScene é protected no Game — reexposto só pra checagem do teste.</summary>
        public string? Boot => BootScene;
    }

    [Fact]
    public void DebugFlag_IsRecognized_EvenAsTheLastArgument()
    {
        // O laço antigo parava em args.Length - 1: uma flag sem valor no fim era engolida, e o
        // jogo abria sem overlay nenhum sem dizer por quê.
        var game = new HeadlessGame();

        game.ParseArgs(["--scene", "cenas/fase1.json", "--debug"]);

        Assert.True(game.DebugOverlayEnabled);
        Assert.Equal("cenas/fase1.json", game.Boot);
    }

    [Fact]
    public void DebugFlag_Alone_Works()
    {
        var game = new HeadlessGame();

        game.ParseArgs(["--debug"]);

        Assert.True(game.DebugOverlayEnabled);
    }

    [Fact]
    public void WithoutTheFlag_OverlayStaysOff()
    {
        var game = new HeadlessGame();

        game.ParseArgs(["--scene", "cenas/fase1.json"]);

        Assert.False(game.DebugOverlayEnabled);
    }

    [Fact]
    public void ValueFlagAtTheEndWithoutItsValue_IsIgnoredInsteadOfCrashing()
    {
        var game = new HeadlessGame();

        game.ParseArgs(["--debug", "--scene"]);

        Assert.True(game.DebugOverlayEnabled);
        Assert.Null(game.Boot);
    }

    [Fact]
    public void Fps_IsAveragedOverTheWindow_NotCountedPerFrame()
    {
        var overlay = new DebugOverlay();

        // 30 frames de 1/60s = meio segundo -> fecha a janela de amostragem em 60 FPS.
        for (int i = 0; i < 30; i++)
            overlay.Tick(1f / 60f);

        Assert.Equal(60, ReadFps(overlay), precision: 0);
    }

    [Fact]
    public void Fps_StaysAtZero_BeforeTheFirstWindowCloses()
    {
        var overlay = new DebugOverlay();

        overlay.Tick(1f / 60f);

        Assert.Equal(0, ReadFps(overlay));
    }

    private static double ReadFps(DebugOverlay overlay)
    {
        var field = typeof(DebugOverlay).GetField("_fps",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (float)field.GetValue(overlay)!;
    }
}
