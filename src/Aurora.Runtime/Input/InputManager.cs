using System.Numerics;
using Silk.NET.Input;

namespace Aurora.Runtime.Input;

/// <summary>
/// Estado de teclado, mouse e gamepad. Consulte <see cref="IsKeyDown"/> para tecla segurada,
/// <see cref="WasKeyPressed"/> para tecla pressionada neste frame, e <see cref="AxisX"/>/
/// <see cref="AxisY"/> pra movimento combinando teclado + analógico esquerdo automaticamente.
/// </summary>
public sealed class InputManager
{
    private readonly IInputContext _context;

    private static readonly Key[] AllKeys = Enum.GetValues<Key>().Where(k => k != Key.Unknown).ToArray();
    private static readonly MouseButton[] AllButtons = Enum.GetValues<MouseButton>().Where(b => b != MouseButton.Unknown).ToArray();
    private static readonly ButtonName[] AllGamepadButtons = Enum.GetValues<ButtonName>().Where(b => b != ButtonName.Unknown).ToArray();

    private const float StickDeadzone = 0.2f;

    private readonly HashSet<Key> _keysDown = new();
    private readonly HashSet<Key> _keysPressedThisFrame = new();
    private readonly HashSet<MouseButton> _buttonsDown = new();
    private readonly HashSet<MouseButton> _buttonsPressedThisFrame = new();
    private readonly HashSet<ButtonName> _gamepadButtonsDown = new();
    private readonly HashSet<ButtonName> _gamepadButtonsPressedThisFrame = new();

    // O toque em Android não chega de forma confiável via IMouse do backend SDL do
    // Silk.NET (mouse sintético de toque é frágil/incompleto nesse binding - confirmado
    // reproduzindo em device real). MainActivity chama SetPointer direto do
    // Activity.OnTouchEvent, contornando o Silk.NET.Input inteiro pro toque.
    private readonly object _pointerLock = new();
    private Vector2? _pointerOverride;
    private bool _pointerOverrideDown;

    // Multi-toque de verdade (joystick + botões simultâneos) — separado do pointer único
    // acima, que continua servindo cliques de UiButton (UIManager só olha um ponto).
    // MainActivity chama SetTouch por dedo (id = MotionEvent.GetPointerId), no desktop
    // fica sempre vazio (sem touchscreen real aqui).
    private readonly Dictionary<int, Vector2> _touches = new();

    // Mapeia pixel bruto da janela pra "espaço de design" quando o jogo usa
    // Game.DesignResolution (letterbox/pillarbox) — identidade (offset 0, escala 1) por
    // padrão, ou seja, zero mudança de comportamento pra jogos que não configuram isso.
    private Vector2 _viewportOffset = Vector2.Zero;
    private Vector2 _viewportScale = Vector2.One;

    public InputManager(IInputContext context)
    {
        _context = context;
    }

    /// <summary>Chamado pelo Game quando usa DesignResolution: converte clique/toque em pixel
    /// físico da janela pra coordenada de design (a mesma que UI/Anchor e ScreenSize usam).</summary>
    internal void SetViewportMapping(int viewportX, int viewportY, int viewportWidth, int viewportHeight,
        int designWidth, int designHeight)
    {
        _viewportOffset = new Vector2(viewportX, viewportY);
        _viewportScale = new Vector2(
            designWidth / (float)Math.Max(1, viewportWidth),
            designHeight / (float)Math.Max(1, viewportHeight));
    }

    /// <summary>Volta ao mapeamento identidade (sem DesignResolution).</summary>
    internal void ClearViewportMapping()
    {
        _viewportOffset = Vector2.Zero;
        _viewportScale = Vector2.One;
    }

    private Vector2 MapToDesignSpace(Vector2 rawWindowPosition) => (rawWindowPosition - _viewportOffset) * _viewportScale;

    // Sempre consultados de novo (não guardados em campo): um FirstOrDefault()
    // cacheado no construtor perderia dispositivo que conecta depois.
    private IKeyboard? Keyboard => _context.Keyboards.FirstOrDefault();
    private IMouse? Mouse => _context.Mice.FirstOrDefault();
    private IGamepad? Gamepad => _context.Gamepads.FirstOrDefault();

    /// <summary>True se algum controle está plugado e reconhecido nesse frame.</summary>
    public bool IsGamepadConnected => Gamepad is not null;

    /// <summary>Chamado pela plataforma (ex: MainActivity Android) com a posição em
    /// coordenadas de tela do toque/clique ativo, ou null quando solto.</summary>
    public void SetPointer(Vector2? screenPosition, bool down)
    {
        lock (_pointerLock)
        {
            _pointerOverride = screenPosition;
            _pointerOverrideDown = down;
        }
    }

    private (Vector2? Position, bool Down) ReadPointerOverride()
    {
        lock (_pointerLock)
            return (_pointerOverride, _pointerOverrideDown);
    }

    /// <summary>Chamado pela plataforma (MainActivity) por dedo — id = MotionEvent.GetPointerId,
    /// down=false remove o toque. Independente de SetPointer (que continua sendo "o" clique
    /// único usado pelo UIManager pra botão de menu/HUD).</summary>
    public void SetTouch(int id, Vector2 position, bool down)
    {
        lock (_pointerLock)
        {
            if (down) _touches[id] = position;
            else _touches.Remove(id);
        }
    }

    /// <summary>Todos os toques ativos agora — ids reais de SetTouch (multi-toque de
    /// verdade, Android). Sem multi-toque disponível (desktop, ou Android antes do primeiro
    /// SetTouch), cai pro pointer único (mouse ou SetPointer) como um toque sintético id -1,
    /// pra dar pra testar joystick/botões sem touchscreen real.</summary>
    public IReadOnlyList<(int Id, Vector2 Position)> ActiveTouches
    {
        get
        {
            lock (_pointerLock)
            {
                if (_touches.Count > 0)
                {
                    var list = new List<(int, Vector2)>(_touches.Count);
                    foreach (var (id, pos) in _touches)
                        list.Add((id, MapToDesignSpace(pos)));
                    return list;
                }
            }

            return IsMouseDown() ? [(-1, MousePosition)] : [];
        }
    }

    public bool IsKeyDown(Key key) => Keyboard?.IsKeyPressed(key) ?? false;
    public bool WasKeyPressed(Key key) => _keysPressedThisFrame.Contains(key);

    public Vector2 MousePosition => MapToDesignSpace(ReadPointerOverride().Position ?? Mouse?.Position ?? Vector2.Zero);

    public bool IsMouseDown(MouseButton button = MouseButton.Left)
        => (button == MouseButton.Left && ReadPointerOverride().Down) || (Mouse?.IsButtonPressed(button) ?? false);

    public bool WasMouseClicked(MouseButton button = MouseButton.Left) => _buttonsPressedThisFrame.Contains(button);

    /// <summary>Eixo horizontal (-1..1) combinando A/D, setas e o analógico esquerdo do gamepad
    /// (o que estiver mais "puxado" no frame vence) — scripts que já usam AxisX ganham suporte
    /// a controle de graça, sem mudar nada.</summary>
    public float AxisX
    {
        get
        {
            float keys = (IsKeyDown(Key.D) || IsKeyDown(Key.Right) ? 1f : 0f)
                       - (IsKeyDown(Key.A) || IsKeyDown(Key.Left) ? 1f : 0f);
            float stick = LeftStick.X;
            return MathF.Abs(stick) > MathF.Abs(keys) ? stick : keys;
        }
    }

    /// <summary>Eixo vertical (-1..1) combinando W/S, setas e o analógico esquerdo do gamepad.
    /// Positivo = baixo (convenção de tela).</summary>
    public float AxisY
    {
        get
        {
            float keys = (IsKeyDown(Key.S) || IsKeyDown(Key.Down) ? 1f : 0f)
                       - (IsKeyDown(Key.W) || IsKeyDown(Key.Up) ? 1f : 0f);
            float stick = LeftStick.Y;
            return MathF.Abs(stick) > MathF.Abs(keys) ? stick : keys;
        }
    }

    /// <summary>Analógico esquerdo (-1..1 por eixo), com deadzone — Y positivo = baixo (mesma
    /// convenção de tela usada em <see cref="AxisY"/>). Vetor zero sem gamepad conectado.</summary>
    public Vector2 LeftStick => ReadStick(0);

    /// <summary>Analógico direito (-1..1 por eixo, mesma convenção de <see cref="LeftStick"/>).</summary>
    public Vector2 RightStick => ReadStick(1);

    private Vector2 ReadStick(int index)
    {
        if (Gamepad is not { } pad)
            return Vector2.Zero;

        foreach (var stick in pad.Thumbsticks)
        {
            if (stick.Index != index)
                continue;

            var v = new Vector2(stick.X, stick.Y);
            return v.LengthSquared() < StickDeadzone * StickDeadzone ? Vector2.Zero : v;
        }

        return Vector2.Zero;
    }

    /// <summary>Gatilho esquerdo (0..1, 0 sem gamepad).</summary>
    public float LeftTrigger => ReadTrigger(0);

    /// <summary>Gatilho direito (0..1, 0 sem gamepad).</summary>
    public float RightTrigger => ReadTrigger(1);

    private float ReadTrigger(int index)
    {
        if (Gamepad is not { } pad)
            return 0f;

        foreach (var trigger in pad.Triggers)
        {
            if (trigger.Index == index)
                return trigger.Position;
        }

        return 0f;
    }

    public bool IsGamepadButtonDown(ButtonName button)
    {
        if (Gamepad is not { } pad)
            return false;

        foreach (var b in pad.Buttons)
        {
            if (b.Name == button)
                return b.Pressed;
        }

        return false;
    }

    public bool WasGamepadButtonPressed(ButtonName button) => _gamepadButtonsPressedThisFrame.Contains(button);

    /// <summary>Chamado pela engine no início de cada frame, antes do OnUpdate do jogo -
    /// via polling (não evento), pra funcionar mesmo com dispositivo que aparece tarde.</summary>
    internal void BeginFrame()
    {
        _keysPressedThisFrame.Clear();
        if (Keyboard is { } keyboard)
        {
            foreach (var key in AllKeys)
            {
                bool down = keyboard.IsKeyPressed(key);
                if (down && _keysDown.Add(key))
                    _keysPressedThisFrame.Add(key);
                else if (!down)
                    _keysDown.Remove(key);
            }
        }

        _buttonsPressedThisFrame.Clear();
        bool pointerDown = ReadPointerOverride().Down;
        var mouse = Mouse;
        foreach (var button in AllButtons)
        {
            bool down = (mouse?.IsButtonPressed(button) ?? false) || (button == MouseButton.Left && pointerDown);
            if (down && _buttonsDown.Add(button))
                _buttonsPressedThisFrame.Add(button);
            else if (!down)
                _buttonsDown.Remove(button);
        }

        _gamepadButtonsPressedThisFrame.Clear();
        var gamepad = Gamepad;
        foreach (var buttonName in AllGamepadButtons)
        {
            bool down = false;
            if (gamepad is not null)
            {
                foreach (var b in gamepad.Buttons)
                {
                    if (b.Name != buttonName)
                        continue;
                    down = b.Pressed;
                    break;
                }
            }

            if (down && _gamepadButtonsDown.Add(buttonName))
                _gamepadButtonsPressedThisFrame.Add(buttonName);
            else if (!down)
                _gamepadButtonsDown.Remove(buttonName);
        }
    }
}
