using System.Numerics;
using Silk.NET.Input;

namespace Aurora.Runtime.Input;

/// <summary>
/// Estado de teclado e mouse. Consulte <see cref="IsKeyDown"/> para tecla segurada
/// e <see cref="WasKeyPressed"/> para tecla pressionada neste frame.
/// </summary>
public sealed class InputManager
{
    private readonly IInputContext _context;

    private static readonly Key[] AllKeys = Enum.GetValues<Key>().Where(k => k != Key.Unknown).ToArray();
    private static readonly MouseButton[] AllButtons = Enum.GetValues<MouseButton>().Where(b => b != MouseButton.Unknown).ToArray();

    private readonly HashSet<Key> _keysDown = new();
    private readonly HashSet<Key> _keysPressedThisFrame = new();
    private readonly HashSet<MouseButton> _buttonsDown = new();
    private readonly HashSet<MouseButton> _buttonsPressedThisFrame = new();

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

    public InputManager(IInputContext context)
    {
        _context = context;
    }

    // Sempre consultados de novo (não guardados em campo): um FirstOrDefault()
    // cacheado no construtor perderia dispositivo que conecta depois.
    private IKeyboard? Keyboard => _context.Keyboards.FirstOrDefault();
    private IMouse? Mouse => _context.Mice.FirstOrDefault();

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
                        list.Add((id, pos));
                    return list;
                }
            }

            return IsMouseDown() ? [(-1, MousePosition)] : [];
        }
    }

    public bool IsKeyDown(Key key) => Keyboard?.IsKeyPressed(key) ?? false;
    public bool WasKeyPressed(Key key) => _keysPressedThisFrame.Contains(key);

    public Vector2 MousePosition => ReadPointerOverride().Position ?? Mouse?.Position ?? Vector2.Zero;

    public bool IsMouseDown(MouseButton button = MouseButton.Left)
        => (button == MouseButton.Left && ReadPointerOverride().Down) || (Mouse?.IsButtonPressed(button) ?? false);

    public bool WasMouseClicked(MouseButton button = MouseButton.Left) => _buttonsPressedThisFrame.Contains(button);

    /// <summary>Eixo horizontal (-1..1) combinando A/D e setas.</summary>
    public float AxisX => (IsKeyDown(Key.D) || IsKeyDown(Key.Right) ? 1f : 0f)
                        - (IsKeyDown(Key.A) || IsKeyDown(Key.Left) ? 1f : 0f);

    /// <summary>Eixo vertical (-1..1) combinando W/S e setas. Positivo = baixo (convenção de tela).</summary>
    public float AxisY => (IsKeyDown(Key.S) || IsKeyDown(Key.Down) ? 1f : 0f)
                        - (IsKeyDown(Key.W) || IsKeyDown(Key.Up) ? 1f : 0f);

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
    }
}
