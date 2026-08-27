using System.Numerics;
using Aurora.Runtime.Input;
using Silk.NET.Input;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Entrada injetada de fora, sem dispositivo do Silk.NET — o caminho do play-in-editor, onde não
/// existe janela própria e quem recebe teclado e mouse é o editor.
///
/// <para>Isto nasceu de um bug concreto: o jogo rodava dentro do editor e desenhava certo, mas
/// clicar no botão dele não fazia nada. Render não depende de entrada, então a falha só aparece
/// quando alguém tenta jogar.</para>
/// </summary>
public sealed class InjectedInputTests
{
    [Fact]
    public void SemDispositivo_TeclaInjetadaFicaPressionada()
    {
        var input = new InputManager();

        Assert.False(input.IsKeyDown(Key.Space));

        input.SetKey(Key.Space, true);
        Assert.True(input.IsKeyDown(Key.Space));

        input.SetKey(Key.Space, false);
        Assert.False(input.IsKeyDown(Key.Space));
    }

    /// <summary>
    /// WasKeyPressed é "apertou NESTE frame" — quem usa é pulo, confirmar diálogo, atalho. Sem o
    /// BeginFrame considerar o injetado, ele ficaria sempre falso e o jogo hospedado pareceria
    /// ignorar o teclado mesmo com a tecla segurada.
    /// </summary>
    [Fact]
    public void WasKeyPressed_SoNoFrameEmQueDesce()
    {
        var input = new InputManager();
        input.SetKey(Key.Space, true);

        input.BeginFrame();
        Assert.True(input.WasKeyPressed(Key.Space));

        // Segurando: continua pressionada, mas não é mais "apertou agora".
        input.BeginFrame();
        Assert.False(input.WasKeyPressed(Key.Space));
        Assert.True(input.IsKeyDown(Key.Space));

        // Solta e aperta de novo: conta como novo aperto.
        input.SetKey(Key.Space, false);
        input.BeginFrame();
        input.SetKey(Key.Space, true);
        input.BeginFrame();
        Assert.True(input.WasKeyPressed(Key.Space));
    }

    [Fact]
    public void PonteiroInjetado_ViraCliqueDeMouse()
    {
        var input = new InputManager();

        input.SetPointer(new Vector2(120f, 80f), down: true);
        input.BeginFrame();

        Assert.Equal(new Vector2(120f, 80f), input.MousePosition);
        Assert.True(input.IsMouseDown());
        Assert.True(input.WasMouseClicked());
    }

    [Fact]
    public void BotaoDireitoInjetado_ELido()
    {
        var input = new InputManager();

        Assert.False(input.IsMouseDown(MouseButton.Right));

        input.SetMouseButton(MouseButton.Right, true);
        input.BeginFrame();

        Assert.True(input.IsMouseDown(MouseButton.Right));
        Assert.True(input.WasMouseClicked(MouseButton.Right));
    }

    [Fact]
    public void TextoInjetado_ChegaEmTypedText()
    {
        var input = new InputManager();
        input.AppendTypedText("ação");

        input.BeginFrame();
        Assert.Equal("ação", input.TypedText);

        // Consumido: o frame seguinte começa limpo.
        input.BeginFrame();
        Assert.Equal("", input.TypedText);
    }

    /// <summary>
    /// Alt+Tab com a tecla apertada: o KeyUp acontece na outra janela e nunca volta. Sem soltar
    /// tudo na perda de foco, o personagem anda sozinho pra sempre.
    /// </summary>
    [Fact]
    public void ClearInjectedInput_SoltaTudo()
    {
        var input = new InputManager();
        input.SetKey(Key.D, true);
        input.SetMouseButton(MouseButton.Right, true);
        input.SetPointer(new Vector2(10f, 10f), down: true);

        input.ClearInjectedInput();
        input.BeginFrame();

        Assert.False(input.IsKeyDown(Key.D));
        Assert.False(input.IsMouseDown(MouseButton.Right));
        Assert.False(input.IsMouseDown());
    }

    /// <summary>AxisX/AxisY são o que quase todo controlador lê — se a injeção não chegasse
    /// neles, andar continuaria quebrado mesmo com IsKeyDown funcionando.</summary>
    [Fact]
    public void EixosRespondemATeclaInjetada()
    {
        var input = new InputManager();

        input.SetKey(Key.D, true);
        Assert.Equal(1f, input.AxisX);

        input.SetKey(Key.D, false);
        input.SetKey(Key.A, true);
        Assert.Equal(-1f, input.AxisX);

        input.SetKey(Key.W, true);
        Assert.Equal(-1f, input.AxisY);   // negativo = pra cima (convenção de tela)
    }

    /// <summary>Sem gamepad conectado o analógico não pode inventar movimento — é o que
    /// garante que o eixo do teclado injetado não seja engolido por um stick fantasma.</summary>
    [Fact]
    public void SemGamepad_AnalogicoFicaZerado()
    {
        var input = new InputManager();

        Assert.False(input.IsGamepadConnected);
        Assert.Equal(Vector2.Zero, input.LeftStick);
        Assert.Equal(0f, input.LeftTrigger);
    }
}
