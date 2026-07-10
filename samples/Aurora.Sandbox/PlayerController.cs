using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Input;

namespace Aurora.Sandbox;

/// <summary>Movimento em 8 direções com WASD/setas.</summary>
public sealed class PlayerController : Behavior
{
    private const float Speed = 260f;

    private readonly InputManager _input;

    public PlayerController(InputManager input)
    {
        _input = input;
    }

    public override void Update(float deltaTime)
    {
        var direction = new Vector2(_input.AxisX, _input.AxisY);
        if (direction == Vector2.Zero)
            return;

        direction = Vector2.Normalize(direction);

        var transform = Get<Transform>()!;
        transform.Position += direction * Speed * deltaTime;

        if (direction.X != 0)
            Get<SpriteRenderer>()!.FlipX = direction.X < 0;
    }
}
