using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;

namespace Aurora.Sandbox;

/// <summary>
/// Movimento em 8 direções com WASD/setas. Sem teclado (Android),
/// segurar o toque move o jogador em direção ao ponto tocado.
/// </summary>
public sealed class PlayerController : Behavior
{
    private const float Speed = 260f;
    private const float ArriveRadius = 12f;

    private readonly InputManager _input;
    private readonly Camera2D _camera;

    public PlayerController(InputManager input, Camera2D camera)
    {
        _input = input;
        _camera = camera;
    }

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>()!;
        var direction = new Vector2(_input.AxisX, _input.AxisY);

        if (direction == Vector2.Zero && _input.IsMouseDown())
        {
            var target = _camera.ScreenToWorld(_input.MousePosition);
            var delta = target - transform.Position;
            if (delta.Length() > ArriveRadius)
                direction = delta;
        }

        if (direction == Vector2.Zero)
            return;

        direction = Vector2.Normalize(direction);
        transform.Position += direction * Speed * deltaTime;

        if (direction.X != 0)
            Get<SpriteRenderer>()!.FlipX = direction.X < 0;
    }
}
