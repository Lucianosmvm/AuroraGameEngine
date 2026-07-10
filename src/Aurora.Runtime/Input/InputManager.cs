using System.Numerics;
using Silk.NET.Input;

namespace Aurora.Runtime.Input;

/// <summary>
/// Estado de teclado e mouse. Consulte <see cref="IsKeyDown"/> para tecla segurada
/// e <see cref="WasKeyPressed"/> para tecla pressionada neste frame.
/// </summary>
public sealed class InputManager
{
    private readonly IKeyboard? _keyboard;
    private readonly IMouse? _mouse;
    private readonly HashSet<Key> _pressedThisFrame = new();
    private readonly HashSet<MouseButton> _clickedThisFrame = new();

    public InputManager(IInputContext context)
    {
        _keyboard = context.Keyboards.FirstOrDefault();
        _mouse = context.Mice.FirstOrDefault();

        if (_keyboard is not null)
            _keyboard.KeyDown += (_, key, _) => _pressedThisFrame.Add(key);
        if (_mouse is not null)
            _mouse.MouseDown += (_, button) => _clickedThisFrame.Add(button);
    }

    public bool IsKeyDown(Key key) => _keyboard?.IsKeyPressed(key) ?? false;
    public bool WasKeyPressed(Key key) => _pressedThisFrame.Contains(key);

    public Vector2 MousePosition => _mouse?.Position ?? Vector2.Zero;
    public bool IsMouseDown(MouseButton button = MouseButton.Left) => _mouse?.IsButtonPressed(button) ?? false;
    public bool WasMouseClicked(MouseButton button = MouseButton.Left) => _clickedThisFrame.Contains(button);

    /// <summary>Eixo horizontal (-1..1) combinando A/D e setas.</summary>
    public float AxisX => (IsKeyDown(Key.D) || IsKeyDown(Key.Right) ? 1f : 0f)
                        - (IsKeyDown(Key.A) || IsKeyDown(Key.Left) ? 1f : 0f);

    /// <summary>Eixo vertical (-1..1) combinando W/S e setas. Positivo = baixo (convenção de tela).</summary>
    public float AxisY => (IsKeyDown(Key.S) || IsKeyDown(Key.Down) ? 1f : 0f)
                        - (IsKeyDown(Key.W) || IsKeyDown(Key.Up) ? 1f : 0f);

    /// <summary>Chamado pela engine ao fim de cada frame de update.</summary>
    internal void EndFrame()
    {
        _pressedThisFrame.Clear();
        _clickedThisFrame.Clear();
    }
}
