using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>Posição, rotação (radianos) e escala de uma entidade no mundo 2D.</summary>
public sealed class Transform : IComponent
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale = Vector2.One;

    public Transform()
    {
    }

    public Transform(float x, float y)
    {
        Position = new Vector2(x, y);
    }

    public Transform(Vector2 position)
    {
        Position = position;
    }
}
