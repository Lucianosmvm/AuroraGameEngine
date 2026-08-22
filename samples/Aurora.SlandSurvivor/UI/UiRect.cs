using System.Numerics;

namespace Aurora.SlandSurvivor.UI;

/// <summary>
/// Retângulo de tela em pixels de projeto. A engine tem um <c>RectF</c>, mas ele descreve
/// recorte de textura e não tem teste de ponto — este aqui existe só para a interface saber
/// se o ponteiro está dentro de um painel.
/// </summary>
public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public Vector2 Position => new(X, Y);

    public Vector2 Size => new(Width, Height);

    public bool Contains(Vector2 point)
        => point.X >= X && point.Y >= Y && point.X <= X + Width && point.Y <= Y + Height;
}
