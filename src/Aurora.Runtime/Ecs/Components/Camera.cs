using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Componente que controla a <see cref="Graphics.Camera2D"/> do jogo.
/// Adicione a uma entidade com Transform para definir a câmera da cena.
/// </summary>
public sealed class CameraController : IComponent
{
    /// <summary>Nome da entidade a seguir. Null = câmera fixada na posição da própria entidade.</summary>
    public string? Follow;

    /// <summary>Velocidade de interpolação para seguir (0 = instantâneo, -1 = lerp desligado).</summary>
    public float FollowSpeed = 5f;

    /// <summary>Zoom aplicado à Camera2D (1 = sem zoom).</summary>
    public float Zoom = 1f;

    /// <summary>Deslocamento fixo sobre a posição alvo (unidades do mundo).</summary>
    public Vector2 Offset;

    /// <summary>Resolução de referência para preview no editor (pixels).</summary>
    public int ViewWidth = 1280;
    public int ViewHeight = 720;

    /// <summary>Se verdadeiro, a posição da câmera é limitada ao retângulo de bounds.</summary>
    public bool ClampBounds;
    public float BoundsX, BoundsY;
    public float BoundsWidth = 1280f, BoundsHeight = 720f;
}
