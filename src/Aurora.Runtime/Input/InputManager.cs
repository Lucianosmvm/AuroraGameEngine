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

    public InputManager(IInputContext context)
    {
        _context = context;
    }

    // Sempre consultados de novo (não guardados em campo): no Android, o "mouse"
    // sintético do toque só passa a existir depois do primeiro toque na tela -
    // um FirstOrDefault() cacheado no construtor ficaria null pra sempre.
    private IKeyboard? Keyboard => _context.Keyboards.FirstOrDefault();
    private IMouse? Mouse => _context.Mice.FirstOrDefault();

    public bool IsKeyDown(Key key) => Keyboard?.IsKeyPressed(key) ?? false;
    public bool WasKeyPressed(Key key) => _keysPressedThisFrame.Contains(key);

    public Vector2 MousePosition => Mouse?.Position ?? Vector2.Zero;
    public bool IsMouseDown(MouseButton button = MouseButton.Left) => Mouse?.IsButtonPressed(button) ?? false;
    public bool WasMouseClicked(MouseButton button = MouseButton.Left) => _buttonsPressedThisFrame.Contains(button);

    /// <summary>Eixo horizontal (-1..1) combinando A/D e setas.</summary>
    public float AxisX => (IsKeyDown(Key.D) || IsKeyDown(Key.Right) ? 1f : 0f)
                        - (IsKeyDown(Key.A) || IsKeyDown(Key.Left) ? 1f : 0f);

    /// <summary>Eixo vertical (-1..1) combinando W/S e setas. Positivo = baixo (convenção de tela).</summary>
    public float AxisY => (IsKeyDown(Key.S) || IsKeyDown(Key.Down) ? 1f : 0f)
                        - (IsKeyDown(Key.W) || IsKeyDown(Key.Up) ? 1f : 0f);

    /// <summary>Chamado pela engine no início de cada frame, antes do OnUpdate do jogo -
    /// via polling (não evento), pra funcionar mesmo com dispositivo que aparece tarde
    /// (mouse sintético do toque no Android).</summary>
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
        if (Mouse is { } mouse)
        {
            foreach (var button in AllButtons)
            {
                bool down = mouse.IsButtonPressed(button);
                if (down && _buttonsDown.Add(button))
                    _buttonsPressedThisFrame.Add(button);
                else if (!down)
                    _buttonsDown.Remove(button);
            }
        }
    }
}
