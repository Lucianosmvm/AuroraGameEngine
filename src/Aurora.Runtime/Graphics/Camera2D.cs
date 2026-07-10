using System.Numerics;

namespace Aurora.Runtime.Graphics;

/// <summary>
/// Câmera ortográfica 2D. <see cref="Position"/> é o ponto do mundo no centro da tela.
/// Y cresce para baixo (convenção de tela).
/// </summary>
public sealed class Camera2D
{
    public Vector2 Position;
    public float Zoom = 1f;

    public int ViewportWidth { get; private set; } = 1;
    public int ViewportHeight { get; private set; } = 1;

    public void SetViewport(int width, int height)
    {
        ViewportWidth = Math.Max(1, width);
        ViewportHeight = Math.Max(1, height);
    }

    /// <summary>Move suavemente em direção ao alvo (para seguir o jogador).</summary>
    public void Follow(Vector2 target, float speed, float deltaTime)
        => Position = Vector2.Lerp(Position, target, Math.Clamp(speed * deltaTime, 0f, 1f));

    public Matrix4x4 GetViewProjection()
    {
        var view = Matrix4x4.CreateTranslation(-Position.X, -Position.Y, 0f)
                 * Matrix4x4.CreateScale(Zoom, Zoom, 1f)
                 * Matrix4x4.CreateTranslation(ViewportWidth / 2f, ViewportHeight / 2f, 0f);

        var projection = Matrix4x4.CreateOrthographicOffCenter(0f, ViewportWidth, ViewportHeight, 0f, -1f, 1f);

        return view * projection;
    }

    /// <summary>Retângulo do mundo visível (culling de tiles/sprites).</summary>
    public (Vector2 Min, Vector2 Max) GetVisibleBounds()
    {
        var half = new Vector2(ViewportWidth, ViewportHeight) / (2f * MathF.Max(Zoom, 0.0001f));
        return (Position - half, Position + half);
    }

    /// <summary>Converte um ponto da tela (pixels) para coordenadas do mundo.</summary>
    public Vector2 ScreenToWorld(Vector2 screen)
    {
        var centered = screen - new Vector2(ViewportWidth / 2f, ViewportHeight / 2f);
        return centered / Zoom + Position;
    }
}
