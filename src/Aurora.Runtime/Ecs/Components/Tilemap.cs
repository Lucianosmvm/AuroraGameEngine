using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Grade de tiles desenhada a partir de um tileset (textura fatiada em células).
/// Índices correm da esquerda para a direita, de cima para baixo; -1 = vazio.
/// A posição do Transform é o canto superior esquerdo da grade.
/// </summary>
public sealed class Tilemap : IComponent
{
    public Texture2D? Tileset;
    public int TileWidth = 16;
    public int TileHeight = 16;

    /// <summary>Dimensões da grade, em tiles.</summary>
    public int Width;
    public int Height;

    /// <summary>Ordem de desenho, mesma escala dos SpriteRenderers.</summary>
    public int Layer;

    /// <summary>Width*Height índices; -1 = célula vazia.</summary>
    public int[] Tiles = [];

    public int TilesPerRow => Tileset is null || TileWidth <= 0
        ? 1
        : Math.Max(1, Tileset.Width / TileWidth);

    public int GetTile(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height && Tiles.Length == Width * Height
            ? Tiles[y * Width + x]
            : -1;

    public void SetTile(int x, int y, int index)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return;

        EnsureSize();
        Tiles[y * Width + x] = index;
    }

    /// <summary>Garante Tiles com Width*Height células (novas nascem vazias).</summary>
    public void EnsureSize()
    {
        int expected = Math.Max(0, Width * Height);
        if (Tiles.Length == expected)
            return;

        var resized = new int[expected];
        Array.Fill(resized, -1);
        Array.Copy(Tiles, resized, Math.Min(Tiles.Length, expected));
        Tiles = resized;
    }

    /// <summary>Recorte do tileset para um índice de tile.</summary>
    public RectF SourceRect(int index)
    {
        int perRow = TilesPerRow;
        return new RectF(index % perRow * TileWidth, index / perRow * TileHeight, TileWidth, TileHeight);
    }
}
