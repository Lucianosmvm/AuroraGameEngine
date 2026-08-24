using Aurora.Runtime.Input;
using Silk.NET.Input;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Leitura do nome do controle escrito na cena. É o ponto que falha calado: antes só nome de
/// tecla era aceito, então "MouseLeft" virava Key.Unknown e o gatilho nunca disparava — sem
/// erro, sem log, sem nada pra investigar.
/// </summary>
public class InputBindingTests
{
    [Theory]
    [InlineData("E", Key.E)]
    [InlineData("Space", Key.Space)]
    [InlineData("Enter", Key.Enter)]
    [InlineData("F1", Key.F1)]
    [InlineData("Number1", Key.Number1)]
    public void KeyNames_AreRecognized(string name, Key expected)
    {
        var binding = InputBinding.Parse(name);

        Assert.Equal(InputKind.Key, binding.Kind);
        Assert.Equal((int)expected, binding.Code);
    }

    [Theory]
    [InlineData("e")]
    [InlineData("space")]
    [InlineData("SPACE")]
    [InlineData("  Space  ")]
    public void NameIsCaseAndWhitespaceForgiving(string name)
    {
        // Quem digita o campo na cena não deveria precisar acertar a caixa exata do enum.
        Assert.NotEqual(InputKind.None, InputBinding.Parse(name).Kind);
    }

    [Theory]
    [InlineData("MouseLeft", MouseButton.Left)]
    [InlineData("MouseRight", MouseButton.Right)]
    [InlineData("MouseMiddle", MouseButton.Middle)]
    [InlineData("mouseleft", MouseButton.Left)]
    public void MouseButtons_AreRecognized(string name, MouseButton expected)
    {
        var binding = InputBinding.Parse(name);

        Assert.Equal(InputKind.Mouse, binding.Kind);
        Assert.Equal((int)expected, binding.Code);
    }

    [Theory]
    [InlineData("GamepadA", ButtonName.A)]
    [InlineData("GamepadB", ButtonName.B)]
    [InlineData("GamepadStart", ButtonName.Start)]
    public void GamepadButtons_AreRecognized(string name, ButtonName expected)
    {
        var binding = InputBinding.Parse(name);

        Assert.Equal(InputKind.Gamepad, binding.Kind);
        Assert.Equal((int)expected, binding.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NaoExiste")]
    [InlineData("MouseXyz")]
    [InlineData("GamepadXyz")]
    public void UnknownNames_ResolveToNothing(string? name)
    {
        Assert.Equal(InputKind.None, InputBinding.Parse(name).Kind);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("42")]
    public void BareNumbers_AreRejected_InsteadOfBecomingAnArbitraryKey(string name)
    {
        // Enum.TryParse aceita número solto como valor do enum. Um "5" na cena é engano de
        // digitação; virar uma tecla qualquer é pior que não funcionar.
        Assert.Equal(InputKind.None, InputBinding.Parse(name).Kind);
    }

    [Fact]
    public void MouseWinsOverAKeyWithTheSamePrefix()
    {
        // Não existe tecla chamada "MouseLeft", mas a ordem importa se um dia existir algo como
        // "Menu": o prefixo só vale quando o resto casa com um botão de verdade.
        Assert.Equal(InputKind.Mouse, InputBinding.Parse("MouseLeft").Kind);
        Assert.Equal(InputKind.None, InputBinding.Parse("Mouse").Kind);
    }

    [Fact]
    public void WithoutAnInputManager_NothingIsEverPressed()
    {
        // World montado à mão (teste, ferramenta) não tem input — não pode estourar.
        Assert.False(InputBinding.WasPressed(null, "E"));
        Assert.False(InputBinding.WasPressed(null, "MouseLeft"));
    }

    [Fact]
    public void EveryNameOfferedByTheEditor_Resolves()
    {
        // A lista que o editor sugere no campo do gatilho não pode conter nome que a engine não
        // entende — seria oferecer uma escolha que não funciona.
        string[] oferecidos =
        [
            "MouseLeft", "MouseRight", "MouseMiddle",
            "GamepadA", "GamepadB", "GamepadX", "GamepadY",
            "Space", "Enter", "Escape", "Tab", "Backspace", "Delete",
            "Left", "Right", "Up", "Down",
            "ShiftLeft", "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight",
            "A", "E", "Q", "Z",
            "Number0", "Number9",
            "F1", "F12",
        ];

        foreach (string name in oferecidos)
            Assert.True(InputBinding.Parse(name).Kind != InputKind.None, $"'{name}' não resolve.");
    }
}
