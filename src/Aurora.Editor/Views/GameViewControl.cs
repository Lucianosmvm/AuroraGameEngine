using System.Diagnostics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Aurora.Editor.Models;
using Aurora.Runtime.Input;
using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

// Silk.NET e Avalonia têm Key e MouseButton com o mesmo nome — sem apelido, todo uso vira
// ambíguo. Aqui o não-qualificado é sempre o do Avalonia (o evento que chega), e o do jogo sai
// marcado.
using SilkKey = Silk.NET.Input.Key;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Aurora.Editor.Views;

/// <summary>
/// O viewport do modo Play: roda o jogo dentro do editor, no contexto de OpenGL que o próprio
/// Avalonia já mantém.
///
/// <para>No Windows esse contexto é GLES 3.0 via ANGLE — o mesmo dialeto do Android, que o
/// <c>SpriteBatch</c> já sabe emitir. É por isso que rodar aqui não exigiu shader novo.</para>
///
/// <para>Este controle é dono do contexto e do relógio; o <see cref="GameHost"/> é dono do
/// assembly e da instância. A separação importa no Stop: o Shutdown do jogo precisa acontecer
/// com o contexto de GL ainda vivo, e só este controle sabe quando isso é verdade.</para>
/// </summary>
internal sealed class GameViewControl : OpenGlControlBase
{
    private readonly Stopwatch _clock = new();
    private GL? _gl;
    private InputManager? _input;
    private bool _gles;
    private double _lastFrameSeconds;
    private Vector2D<int> _lastSize;
    private bool _gameInitialized;

    private GameHost? _host;

    /// <summary>
    /// De onde vem o jogo. Quem inicializa é o próximo frame, já com o contexto corrente — mas
    /// esse próximo frame precisa ser pedido AQUI.
    ///
    /// <para>Sem o pedido, o controle fica dormente: ele existe desde que a janela abriu, então
    /// já renderizou uma vez sem host, saiu cedo e — por não ter pedido outro frame — nada volta
    /// a chamá-lo quando o host enfim chega. O jogo carrega, nunca inicializa, e o viewport fica
    /// preto sem erro nenhum. Já funcionou por acaso, apoiado no repaint que o IsVisible provoca
    /// ao entrar no modo Play; acaso não é contrato.</para>
    /// </summary>
    public GameHost? Host
    {
        get => _host;
        set
        {
            _host = value;

            if (value is not null)
                RequestNextFrameRendering();
        }
    }

    /// <summary>Pausa a lógica sem parar o desenho: o frame continua sendo pintado, então dá
    /// pra olhar o estado congelado enquanto se mexe no inspector.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Última falha vinda do jogo (init ou frame). O modo Play desliga quando isso
    /// enche — um jogo que explode todo frame não pode virar enxurrada de log.</summary>
    public string? LastError { get; private set; }

    /// <summary>Avisa que o jogo morreu e o modo Play deve sair.</summary>
    public event Action<string>? GameFaulted;

    public GameViewControl()
    {
        // Um controle não-focável nunca recebe KeyDown, e o jogo rodaria mudo pro teclado.
        Focusable = true;
    }

    /// <summary>
    /// Pinta um retângulo transparente do tamanho do controle. Não é enfeite: o que aparece na
    /// tela vem do OpenGL, e o hit-test do Avalonia não enxerga isso — sem nada desenhado pelo
    /// lado gerenciado, o clique atravessa o viewport e vai parar no painel de trás. O jogo
    /// desenha normalmente e simplesmente não responde ao mouse.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        base.Render(context);
    }

    protected override void OnOpenGlInit(GlInterface glInterface)
    {
        // Silk.NET só precisa resolver ponteiro de função, e o Avalonia expõe exatamente isso.
        _gl = GL.GetApi(new LamdaNativeContext(name => glInterface.GetProcAddress(name)));

        string version = _gl.GetStringS(StringName.Version) ?? "";
        _gles = version.Contains("ES", StringComparison.OrdinalIgnoreCase);

        _clock.Restart();
        _lastFrameSeconds = 0d;
    }

    protected override void OnOpenGlDeinit(GlInterface glInterface)
    {
        // Enquanto o contexto ainda vale: depois daqui, liberar textura/buffer é uso indevido.
        StopGame();
        _gl = null;
    }

    protected override void OnOpenGlRender(GlInterface glInterface, int fb)
    {
        if (_gl is null || Host?.Game is not { } game)
            return;

        var size = CurrentSize();

        try
        {
            if (!_gameInitialized)
            {
                _input = new InputManager();

                // O jogo não é dono da janela: pedir pra sair encerra o modo Play, não o editor.
                game.ExitHandler = () => GameFaulted?.Invoke("O jogo pediu pra sair.");

                game.Initialize(_gl, _input, _gles, size);
                _lastSize = size;
                _gameInitialized = true;
            }
            else if (size != _lastSize)
            {
                game.Resize(size);
                _lastSize = size;
            }

            float dt = NextDelta();

            if (!IsPaused)
                game.Tick(dt);

            // Desenha mesmo pausado — senão o viewport apagaria ao pausar.
            game.RenderFrame(IsPaused ? 0f : dt);
        }
        catch (Exception ex)
        {
            Fault(ex);
            return;
        }

        // Sem isto o Avalonia só redesenha quando algo o invalida, e o jogo andaria a passos.
        RequestNextFrameRendering();
    }

    /// <summary>
    /// Tempo real entre frames, com o mesmo teto que o jogo publicado usa por baixo (Game.Tick
    /// clampeia): uma parada no depurador não pode virar um salto de meio segundo na física.
    /// </summary>
    private float NextDelta()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double delta = now - _lastFrameSeconds;
        _lastFrameSeconds = now;
        return (float)delta;
    }

    private Vector2D<int> CurrentSize()
    {
        double scaling = VisualRoot?.RenderScaling ?? 1d;
        return new Vector2D<int>(
            Math.Max(1, (int)(Bounds.Width * scaling)),
            Math.Max(1, (int)(Bounds.Height * scaling)));
    }

    /// <summary>Uma exceção que escapou do jogo derruba o modo Play, não o editor: o script é
    /// código do usuário em edição, quebrar é esperado.</summary>
    private void Fault(Exception ex)
    {
        LastError = ex.ToString();
        Console.Error.WriteLine($"[GameView] Jogo falhou: {ex}");

        StopGame();
        GameFaulted?.Invoke(ex.Message);
    }

    /// <summary>Encerra o jogo com o contexto de GL ainda válido. Idempotente.</summary>
    public void StopGame()
    {
        if (Host is null)
            return;

        Host.Stop(shutdownGame: _gameInitialized && _gl is not null);
        _gameInitialized = false;
        _input = null;
    }

    // -------------------------------------------------------------------------------------
    // Entrada
    //
    // Hospedado não existe janela do Silk.NET, logo não existe teclado nem mouse pro
    // InputManager consultar: quem recebe os eventos é este controle, e repassa. É o mesmo
    // canal que o Android usa pro toque, estendido pra teclado e botão.
    // -------------------------------------------------------------------------------------

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SendPointer(e, e.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Sem o foco, tecla nenhuma chega — e um jogo que responde ao mouse mas não ao teclado
        // até alguém clicar em outro lugar é dos bugs mais confusos de diagnosticar.
        Focus();

        var props = e.GetCurrentPoint(this).Properties;
        SendPointer(e, props.IsLeftButtonPressed);

        if (props.IsRightButtonPressed)
            _input?.SetMouseButton(SilkMouseButton.Right, true);
        if (props.IsMiddleButtonPressed)
            _input?.SetMouseButton(SilkMouseButton.Middle, true);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        SendPointer(e, down: false);

        _input?.SetMouseButton(SilkMouseButton.Right, false);
        _input?.SetMouseButton(SilkMouseButton.Middle, false);
    }

    /// <summary>
    /// Manda a posição em pixels do framebuffer, não em unidades de layout do Avalonia: é nessa
    /// escala que o jogo pensa, e num monitor com escala de tela (125%, 150%) as duas divergem —
    /// o clique cairia deslocado, e cada vez mais longe quanto mais à direita da tela.
    /// </summary>
    private void SendPointer(PointerEventArgs e, bool down)
    {
        if (_input is null)
            return;

        var position = e.GetPosition(this);
        double scaling = VisualRoot?.RenderScaling ?? 1d;

        _input.SetPointer(
            new System.Numerics.Vector2((float)(position.X * scaling), (float)(position.Y * scaling)),
            down);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_input is not null && TryMapKey(e.Key, out var key))
        {
            _input.SetKey(key, true);

            // Setas e Tab moveriam o foco pra outro controle do editor no meio da jogatina.
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (_input is not null && TryMapKey(e.Key, out var key))
            _input.SetKey(key, false);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        // Campo de texto dentro do jogo: só o evento de texto sabe layout, acento e maiúscula.
        if (e.Text is { Length: > 0 } text)
            _input?.AppendTypedText(text);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);

        // Alt+Tab com uma tecla apertada deixaria o personagem andando sozinho pra sempre: o
        // KeyUp acontece na outra janela e nunca chega aqui.
        _input?.ClearInjectedInput();
    }

    /// <summary>
    /// Traduz tecla do Avalonia pra tecla do Silk.NET. A maioria dos nomes bate, e o que não bate
    /// está na tabela — <c>Return</c>/<c>Enter</c> e os dígitos (<c>D1</c>/<c>Number1</c>) são os
    /// que mais doem, porque são justamente pulo, confirmar e atalho de item.
    /// </summary>
    internal static bool TryMapKey(Key avalonia, out SilkKey silk)
    {
        if (Renames.TryGetValue(avalonia, out silk))
            return true;

        return Enum.TryParse(avalonia.ToString(), ignoreCase: false, out silk)
            && silk != SilkKey.Unknown;
    }

    /// <summary>
    /// Só o que os dois enums escrevem diferente. O Silk.NET segue os nomes do GLFW e o Avalonia
    /// os do Windows, então divergem em blocos inteiros — teclado numérico, pontuação e
    /// modificadores. O que bate por nome (letras, F1-F12, setas) não entra aqui.
    /// </summary>
    private static readonly Dictionary<Key, SilkKey> Renames = new()
    {
        [Key.Return] = SilkKey.Enter,
        [Key.Back] = SilkKey.Backspace,
        [Key.Capital] = SilkKey.CapsLock,
        [Key.Next] = SilkKey.PageDown,
        [Key.Scroll] = SilkKey.ScrollLock,
        [Key.Print] = SilkKey.PrintScreen,
        [Key.Apps] = SilkKey.Menu,

        // Fileira de números: no Avalonia é D0-D9, no Silk é Number0-Number9.
        [Key.D0] = SilkKey.Number0,
        [Key.D1] = SilkKey.Number1,
        [Key.D2] = SilkKey.Number2,
        [Key.D3] = SilkKey.Number3,
        [Key.D4] = SilkKey.Number4,
        [Key.D5] = SilkKey.Number5,
        [Key.D6] = SilkKey.Number6,
        [Key.D7] = SilkKey.Number7,
        [Key.D8] = SilkKey.Number8,
        [Key.D9] = SilkKey.Number9,

        // Modificadores: lado antes do nome num, depois do nome no outro.
        [Key.LeftShift] = SilkKey.ShiftLeft,
        [Key.RightShift] = SilkKey.ShiftRight,
        [Key.LeftCtrl] = SilkKey.ControlLeft,
        [Key.RightCtrl] = SilkKey.ControlRight,
        [Key.LeftAlt] = SilkKey.AltLeft,
        [Key.RightAlt] = SilkKey.AltRight,
        [Key.LWin] = SilkKey.SuperLeft,
        [Key.RWin] = SilkKey.SuperRight,

        // Teclado numérico: NumPad no Avalonia, Keypad no Silk.
        [Key.NumPad0] = SilkKey.Keypad0,
        [Key.NumPad1] = SilkKey.Keypad1,
        [Key.NumPad2] = SilkKey.Keypad2,
        [Key.NumPad3] = SilkKey.Keypad3,
        [Key.NumPad4] = SilkKey.Keypad4,
        [Key.NumPad5] = SilkKey.Keypad5,
        [Key.NumPad6] = SilkKey.Keypad6,
        [Key.NumPad7] = SilkKey.Keypad7,
        [Key.NumPad8] = SilkKey.Keypad8,
        [Key.NumPad9] = SilkKey.Keypad9,
        [Key.Multiply] = SilkKey.KeypadMultiply,
        [Key.Add] = SilkKey.KeypadAdd,
        [Key.Subtract] = SilkKey.KeypadSubtract,
        [Key.Divide] = SilkKey.KeypadDivide,
        [Key.Decimal] = SilkKey.KeypadDecimal,

        // Pontuação: o Avalonia nomeia pela posição no teclado americano (Oem*), o Silk pelo
        // símbolo. Vale pra atalho de item e pra campo de texto dentro do jogo.
        [Key.OemPlus] = SilkKey.Equal,
        [Key.OemMinus] = SilkKey.Minus,
        [Key.OemComma] = SilkKey.Comma,
        [Key.OemPeriod] = SilkKey.Period,
        [Key.OemQuestion] = SilkKey.Slash,
        [Key.OemSemicolon] = SilkKey.Semicolon,
        [Key.OemQuotes] = SilkKey.Apostrophe,
        [Key.Oem3] = SilkKey.GraveAccent,
        [Key.Oem4] = SilkKey.LeftBracket,
        [Key.OemCloseBrackets] = SilkKey.RightBracket,
        [Key.OemPipe] = SilkKey.BackSlash,
        [Key.OemBackslash] = SilkKey.BackSlash,
    };
}
