using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>Desenha uma textura na posição do <see cref="Transform"/> da entidade.</summary>
public sealed class SpriteRenderer : IComponent
{
    public Texture2D? Texture;
    public Color Color = Color.White;

    /// <summary>Pivô normalizado. Padrão 0.5,0.5 = centro do sprite.</summary>
    public Vector2 Origin = new(0.5f, 0.5f);

    /// <summary>Tamanho em pixels do mundo. Null = tamanho natural da textura.</summary>
    public Vector2? Size;

    /// <summary>Ordem de desenho: camadas menores são desenhadas primeiro (ficam atrás).</summary>
    public int Layer;

    public bool FlipX;
    public bool FlipY;
    public bool Visible = true;

    public SpriteRenderer()
    {
    }

    public SpriteRenderer(Texture2D texture, int layer = 0)
    {
        Texture = texture;
        Layer = layer;
    }
}
