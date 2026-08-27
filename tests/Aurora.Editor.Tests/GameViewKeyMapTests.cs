using Avalonia.Input;
using Aurora.Editor.Views;
using SilkKey = Silk.NET.Input.Key;

namespace Aurora.Editor.Tests;

/// <summary>
/// A tradução de tecla do Avalonia pro Silk.NET no Play embutido. A maioria dos nomes bate e cai
/// no fallback por nome; o risco está justamente nos que NÃO batem, porque falham em silêncio —
/// a tecla simplesmente não faz nada, sem erro nenhum pra investigar.
/// </summary>
public sealed class GameViewKeyMapTests
{
    [Theory]
    // Os que divergem de nome — cada um destes é uma tecla que ficaria morta sem a tabela.
    [InlineData(Key.Return, SilkKey.Enter)]
    [InlineData(Key.D0, SilkKey.Number0)]
    [InlineData(Key.D1, SilkKey.Number1)]
    [InlineData(Key.D9, SilkKey.Number9)]
    [InlineData(Key.LeftShift, SilkKey.ShiftLeft)]
    [InlineData(Key.RightShift, SilkKey.ShiftRight)]
    [InlineData(Key.LeftCtrl, SilkKey.ControlLeft)]
    [InlineData(Key.LeftAlt, SilkKey.AltLeft)]
    [InlineData(Key.Next, SilkKey.PageDown)]
    [InlineData(Key.Back, SilkKey.Backspace)]
    [InlineData(Key.NumPad5, SilkKey.Keypad5)]
    [InlineData(Key.Add, SilkKey.KeypadAdd)]
    [InlineData(Key.OemPlus, SilkKey.Equal)]
    [InlineData(Key.OemMinus, SilkKey.Minus)]
    [InlineData(Key.OemComma, SilkKey.Comma)]
    [InlineData(Key.Oem3, SilkKey.GraveAccent)]
    [InlineData(Key.LWin, SilkKey.SuperLeft)]
    // Os que batem por nome — confirmam que o fallback funciona e a tabela não precisa crescer.
    [InlineData(Key.Space, SilkKey.Space)]
    [InlineData(Key.Escape, SilkKey.Escape)]
    [InlineData(Key.W, SilkKey.W)]
    [InlineData(Key.Left, SilkKey.Left)]
    [InlineData(Key.Up, SilkKey.Up)]
    [InlineData(Key.F5, SilkKey.F5)]
    [InlineData(Key.Tab, SilkKey.Tab)]
    public void TraduzTecla(Key avalonia, SilkKey esperada)
    {
        Assert.True(GameViewControl.TryMapKey(avalonia, out var silk),
            $"{avalonia} não foi traduzida — a tecla ficaria morta no Play embutido.");
        Assert.Equal(esperada, silk);
    }

    /// <summary>Tecla sem correspondente não pode virar Unknown "válido": o jogo receberia
    /// apertos de uma tecla que não existe.</summary>
    [Fact]
    public void TeclaSemCorrespondente_NaoTraduz()
    {
        Assert.False(GameViewControl.TryMapKey(Key.None, out _));
    }

    /// <summary>As teclas que os controladores da engine leem por padrão. Se alguma parar de
    /// traduzir, andar ou pular quebra no editor e continua funcionando na janela — divergência
    /// péssima de diagnosticar.</summary>
    [Theory]
    [InlineData(Key.W)]
    [InlineData(Key.A)]
    [InlineData(Key.S)]
    [InlineData(Key.D)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.Space)]
    [InlineData(Key.Return)]
    [InlineData(Key.Escape)]
    // Edição de texto dentro do jogo (campo de nome, console de debug).
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    [InlineData(Key.Home)]
    [InlineData(Key.End)]
    public void TeclasDeJogoTraduzem(Key key)
        => Assert.True(GameViewControl.TryMapKey(key, out _));
}
