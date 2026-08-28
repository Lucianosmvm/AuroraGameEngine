using Aurora.Runtime.Ecs;
using Aurora.Runtime.Events;
using Aurora.Runtime.Input;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// O espelhamento entre <see cref="UiTextInput.Variable"/> e a variável de texto do jogo — o que
/// faz "pede o nome e usa depois" existir sem uma linha de código do usuário.
/// </summary>
public sealed class TextInputBindingTests
{
    private const float Width = 1280f;
    private const float Height = 720f;

    private static (UIManager Ui, GameState State, UiTextInput Field, InputManager Input) Build(
        string variable = "NomeJogador", string initialText = "")
    {
        var state = new GameState();
        var field = new UiTextInput { Name = "Campo", Variable = variable, Text = initialText };
        var screen = new UiScreen("menu") { Visible = true };
        screen.Elements.Add(field);

        var ui = new UIManager { State = state };
        ui.Add(screen);

        return (ui, state, field, new InputManager());
    }

    private static void Tick(UIManager ui, InputManager input, EventSystem? events = null)
    {
        input.BeginFrame();
        ui.Update(input, events, Width, Height);
    }

    [Fact]
    public void OQueODigitadoViraVariavel()
    {
        var (ui, state, field, input) = Build();

        field.Text = "Ana";
        Tick(ui, input);

        Assert.Equal("Ana", state.GetText("NomeJogador"));
    }

    /// <summary>
    /// Escrever só no Enter seria a armadilha: quase toda tela confirma por botão, e o valor
    /// sumiria calado. Este teste trava o comportamento de gravar a cada mudança.
    /// </summary>
    [Fact]
    public void GravaSemPrecisarDeEnter()
    {
        var (ui, state, field, input) = Build();

        // Primeiro frame estabelece o vínculo; depois cada letra já vale.
        Tick(ui, input);

        field.Text = "A";
        Tick(ui, input);
        Assert.Equal("A", state.GetText("NomeJogador"));

        field.Text = "An";
        Tick(ui, input);
        Assert.Equal("An", state.GetText("NomeJogador"));
    }

    /// <summary>Sentido inverso: carregar um save tem que reaparecer no campo.</summary>
    [Fact]
    public void VariavelMudadaDeFora_VoltaProCampo()
    {
        var (ui, state, field, input) = Build();
        Tick(ui, input);

        state.SetText("NomeJogador", "Beto");
        Tick(ui, input);

        Assert.Equal("Beto", field.Text);
    }

    /// <summary>
    /// Um campo com texto padrão na cena não pode apagar o nome que veio do save. A variável
    /// existente ganha do padrão no primeiro encontro.
    /// </summary>
    [Fact]
    public void VariavelExistente_GanhaDoTextoPadraoDaCena()
    {
        var (ui, state, field, input) = Build(initialText: "Herói");
        state.SetText("NomeJogador", "Ana");

        Tick(ui, input);

        Assert.Equal("Ana", field.Text);
        Assert.Equal("Ana", state.GetText("NomeJogador"));
    }

    /// <summary>Sem valor guardado, o texto da cena vira o inicial — senão um padrão escrito no
    /// editor não apareceria em lugar nenhum.</summary>
    [Fact]
    public void SemVariavel_TextoDaCenaViraOValorInicial()
    {
        var (ui, state, field, input) = Build(initialText: "Herói");

        Tick(ui, input);

        Assert.Equal("Herói", state.GetText("NomeJogador"));
        Assert.Equal("Herói", field.Text);
    }

    /// <summary>Campo sem Variable segue solto, lido só por código — é o comportamento que já
    /// existia e não pode mudar.</summary>
    [Fact]
    public void CampoSemVariable_NaoEscreveNada()
    {
        var (ui, state, field, input) = Build(variable: "");

        field.Text = "Ana";
        Tick(ui, input);

        Assert.Empty(state.Texts);
    }

    /// <summary>O ciclo inteiro do caso do usuário: digita o nome, e uma condição de evento
    /// compara com ele.</summary>
    [Fact]
    public void NomeDigitado_ServeNumaCondicao()
    {
        var (ui, state, field, input) = Build();
        var events = new EventSystem(new World(), state);

        field.Text = "Ana";
        Tick(ui, input, events);

        Assert.True(events.TestCondition(new EventAction
        {
            Name = "NomeJogador",
            Text = "Text",
            Op = "==",
            TextValue = "Ana",
        }));

        Assert.False(events.TestCondition(new EventAction
        {
            Name = "NomeJogador",
            Text = "Text",
            Op = "==",
            TextValue = "Beto",
        }));
    }
}
