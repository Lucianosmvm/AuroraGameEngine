using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Events;
using Aurora.Runtime.Input;
using Aurora.Runtime.Saves;
using Aurora.Runtime.UI;

namespace Aurora.Runtime.Tests;

/// <summary>
/// A barra arrastável — o primeiro elemento de UI que trata arrasto, e não só clique.
/// </summary>
public sealed class UiSliderTests : IDisposable
{
    private const float Width = 1280f;
    private const float Height = 720f;

    // Slider em (100,100), 200 de largura, alça de 10: a faixa útil vai de x=105 a x=295.
    private const float SliderX = 100f;
    private const float SliderWidth = 200f;
    private const float KnobWidth = 10f;

    private readonly string _dir;

    public UiSliderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aurora-slider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private (UIManager Ui, UiSlider Slider, InputManager Input, GameSettings Settings, GameState State) Build(
        string setting = GameSettings.MasterVolume, string variable = "", float step = 0f)
    {
        var slider = new UiSlider
        {
            Name = "Volume",
            X = SliderX,
            Y = 100f,
            Width = SliderWidth,
            Height = 20f,
            KnobWidth = KnobWidth,
            Setting = setting,
            Variable = variable,
            Min = 0f,
            Max = 1f,
            Step = step,
            Default = 1f,
        };

        var screen = new UiScreen("opcoes") { Visible = true };
        screen.Elements.Add(slider);

        var settings = new GameSettings(Path.Combine(_dir, "settings.json"));
        var state = new GameState();
        var ui = new UIManager { Settings = settings, State = state };
        ui.Add(screen);

        return (ui, slider, new InputManager(), settings, state);
    }

    /// <summary>Um frame com o ponteiro apertado na posição dada.</summary>
    private static void Press(UIManager ui, InputManager input, float x, float y = 108f)
    {
        input.SetPointer(new Vector2(x, y), down: true);
        input.BeginFrame();
        ui.Update(input, null, Width, Height);
    }

    private static void Release(UIManager ui, InputManager input)
    {
        input.SetPointer(null, down: false);
        input.BeginFrame();
        ui.Update(input, null, Width, Height);
    }

    [Fact]
    public void ClicarNoMeio_ColocaOValorNoMeio()
    {
        var (ui, slider, input, settings, _) = Build();

        // Centro da faixa útil: 105 + 190/2 = 200.
        Press(ui, input, 200f);

        Assert.Equal(0.5f, slider.Value, 2);
        Assert.Equal(0.5f, settings.Get(GameSettings.MasterVolume, 1f), 2);
    }

    /// <summary>Clicar em qualquer ponto da barra vale, não só na alça — acertar 10px é ruim no
    /// mouse e pior no dedo.</summary>
    [Fact]
    public void ClicarNaBarra_JaComecaOArrasto()
    {
        var (ui, slider, input, _, _) = Build();

        Press(ui, input, 150f);

        Assert.True(slider.Value > 0f);
        Assert.True(slider.Value < 0.5f);
    }

    /// <summary>Os extremos precisam ser alcançáveis: se a fração fosse medida na barra inteira,
    /// o centro da alça nunca chegaria à borda e ninguém conseguiria mudo nem volume cheio.</summary>
    [Fact]
    public void ExtremosSaoAlcancaveis()
    {
        var (ui, slider, input, _, _) = Build();

        Press(ui, input, SliderX);
        Assert.Equal(0f, slider.Value, 3);

        Press(ui, input, SliderX + SliderWidth);
        Assert.Equal(1f, slider.Value, 3);
    }

    /// <summary>Quem arrasta um volume não mantém o dedo dentro da barra. Soltar o valor no meio
    /// do gesto é a diferença entre um controle usável e um que "escapa".</summary>
    [Fact]
    public void ArrastoContinuaComPonteiroForaDoElemento()
    {
        var (ui, slider, input, _, _) = Build();

        Press(ui, input, 200f);
        Press(ui, input, 250f, y: 400f);   // bem abaixo do slider

        Assert.True(slider.Value > 0.5f);
    }

    /// <summary>Soltar mantém o valor — o oposto do joystick, que volta ao centro.</summary>
    [Fact]
    public void SoltarMantemOValor()
    {
        var (ui, slider, input, _, _) = Build();

        Press(ui, input, 200f);
        float durante = slider.Value;
        Release(ui, input);

        Assert.Equal(durante, slider.Value);
    }

    /// <summary>Arrastar pra muito além da barra para nos extremos, não dá a volta nem
    /// extrapola. Começa dentro: um toque que nasce fora nem chega ao slider.</summary>
    [Fact]
    public void ArrastarParaForaParaNosExtremos()
    {
        var (ui, slider, input, _, _) = Build();

        Press(ui, input, 200f);
        Press(ui, input, -500f);
        Assert.Equal(0f, slider.Value);

        Press(ui, input, 5000f);
        Assert.Equal(1f, slider.Value);
    }

    /// <summary>Toque que nasce fora da barra não mexe nela — senão clicar em qualquer canto da
    /// tela mudaria o volume.</summary>
    [Fact]
    public void ToqueForaDaBarra_NaoMexeNoValor()
    {
        var (ui, slider, input, settings, _) = Build();

        Press(ui, input, 700f, y: 500f);

        Assert.Equal(1f, slider.Value);          // segue no Default
        Assert.Empty(settings.Values);           // nada foi gravado
    }

    [Fact]
    public void StepArredondaOValor()
    {
        var (ui, slider, input, _, _) = Build(step: 0.25f);

        Press(ui, input, 200f);      // meio = 0.5, já múltiplo
        Assert.Equal(0.5f, slider.Value, 3);

        Press(ui, input, 160f);      // ~0.29 -> arredonda pra 0.25
        Assert.Equal(0.25f, slider.Value, 3);
    }

    /// <summary>Abrir o menu tem que mostrar o volume já escolhido, não o padrão.</summary>
    [Fact]
    public void AbreNaPosicaoDoValorGuardado()
    {
        var (ui, slider, input, settings, _) = Build();
        settings.Set(GameSettings.MasterVolume, 0.25f);

        input.BeginFrame();
        ui.Update(input, null, Width, Height);

        Assert.Equal(0.25f, slider.Value, 3);
    }

    /// <summary>Sem valor guardado, usa o Default — volume quer 1, senão o jogo abre mudo.</summary>
    [Fact]
    public void SemValorGuardado_UsaODefault()
    {
        var (ui, slider, input, _, _) = Build();

        input.BeginFrame();
        ui.Update(input, null, Width, Height);

        Assert.Equal(1f, slider.Value);
    }

    /// <summary>Ligado em Variable escreve no GameState (entra no save), não nas preferências.</summary>
    [Fact]
    public void LigadoEmVariable_EscreveNoGameState()
    {
        var (ui, _, input, settings, state) = Build(setting: "", variable: "Dificuldade");

        Press(ui, input, 200f);

        Assert.Equal(0.5f, state.GetVariable("Dificuldade"), 2);
        Assert.Empty(settings.Values);
    }

    /// <summary>A cadeia inteira do menu de volume: ação de evento grava a preferência, e o
    /// slider da tela reflete.</summary>
    [Fact]
    public void AcaoSetVolume_ChegaNoSliderENasPreferencias()
    {
        var (ui, slider, input, settings, state) = Build();
        var events = new EventSystem(new World(), state) { Settings = settings };

        events.RunActions([new EventAction { Type = "SetVolume", Name = "Master", Value = 0.4f }]);

        input.BeginFrame();
        ui.Update(input, null, Width, Height);

        Assert.Equal(0.4f, settings.Get(GameSettings.MasterVolume, 1f), 3);
        Assert.Equal(0.4f, slider.Value, 3);
    }

    [Fact]
    public void AcaoSetVolume_EscolheOCanal()
    {
        var (_, _, _, settings, state) = Build();
        var events = new EventSystem(new World(), state) { Settings = settings };

        events.RunActions([
            new EventAction { Type = "SetVolume", Name = "Music", Value = 0.2f },
            new EventAction { Type = "SetVolume", Name = "Sfx", Value = 0.8f },
        ]);

        Assert.Equal(0.2f, settings.Get(GameSettings.MusicVolume, 1f), 3);
        Assert.Equal(0.8f, settings.Get(GameSettings.SfxVolume, 1f), 3);
        Assert.False(settings.Has(GameSettings.MasterVolume));
    }

    [Fact]
    public void AcaoSetVolume_LimitaEntreZeroEUm()
    {
        var (_, _, _, settings, state) = Build();
        var events = new EventSystem(new World(), state) { Settings = settings };

        events.RunActions([new EventAction { Type = "SetVolume", Name = "Master", Value = 5f }]);

        Assert.Equal(1f, settings.Get(GameSettings.MasterVolume, 0f));
    }
}
