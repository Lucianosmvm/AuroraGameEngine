using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

public enum ColliderShape { Box, Circle }

/// <summary>
/// Resultado de uma colisão sólida, do ponto de vista de quem recebe o callback.
/// Normal aponta para fora da outra entidade (direção em que esta entidade deve se mover para sair).
/// </summary>
public readonly struct CollisionInfo
{
    public readonly Vector2 Normal;
    public readonly float Depth;

    public CollisionInfo(Vector2 normal, float depth)
    {
        Normal = normal;
        Depth = depth;
    }
}

/// <summary>
/// Forma de colisão de uma entidade.
/// IsSolid=true → empurra outros sólidos (parede, personagem).
/// IsSolid=false → trigger, dispara OnTriggerEnter/Exit sem bloqueio de movimento.
/// IsKinematic=true → não é movido pela resolução (paredes, objetos estáticos).
/// </summary>
public sealed class Collider : IComponent
{
    /// <summary>Box usa Width/Height; Circle usa Radius.</summary>
    public ColliderShape Shape = ColliderShape.Box;

    /// <summary>Largura do AABB em pixels.</summary>
    public float Width = 16f;

    /// <summary>Altura do AABB em pixels.</summary>
    public float Height = 16f;

    /// <summary>Raio em pixels, usado quando Shape=Circle.</summary>
    public float Radius = 8f;

    /// <summary>Deslocamento do centro do Transform (hitbox deslocada).</summary>
    public Vector2 Offset;

    /// <summary>True = empurra entidades não-cinemáticas. False = trigger (só callbacks).</summary>
    public bool IsSolid = true;

    /// <summary>True = não é deslocado pela resolução de colisão (paredes, obstáculos estáticos).</summary>
    public bool IsKinematic = false;

    /// <summary>Camada desta entidade (bit flags, 1–30).</summary>
    public int Layer = 1;

    /// <summary>Com quais camadas este collider interage (-1 = todas).</summary>
    public int Mask = ~0;
}
