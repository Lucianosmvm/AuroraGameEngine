using System.Numerics;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.AI;

/// <summary>Uma célula da grade de navegação (linha/coluna, não posição de mundo).</summary>
public readonly record struct GridPos(int X, int Y);

/// <summary>
/// Grade de navegação derivada de um Tilemap: célula bloqueada = índice de tile em
/// SolidTiles (mesma fonte de verdade que já bloqueia colisão no World). Não há grade
/// separada pra autorar — pathfinding anda por cima da mesma tilemap que já pinta o chão.
/// </summary>
public sealed class NavGrid
{
    public int Width { get; }
    public int Height { get; }
    public float CellWidth { get; }
    public float CellHeight { get; }
    public Vector2 Origin { get; }

    private readonly bool[] _blocked;

    private NavGrid(int width, int height, float cellWidth, float cellHeight, Vector2 origin, bool[] blocked)
    {
        Width = width;
        Height = height;
        CellWidth = cellWidth;
        CellHeight = cellHeight;
        Origin = origin;
        _blocked = blocked;
    }

    public bool IsBlocked(GridPos cell)
        => cell.X < 0 || cell.Y < 0 || cell.X >= Width || cell.Y >= Height
           || _blocked[cell.Y * Width + cell.X];

    public Vector2 CellToWorld(GridPos cell)
        => Origin + new Vector2((cell.X + 0.5f) * CellWidth, (cell.Y + 0.5f) * CellHeight);

    public GridPos WorldToCell(Vector2 world)
    {
        var local = world - Origin;
        return new GridPos(
            (int)MathF.Floor(local.X / CellWidth),
            (int)MathF.Floor(local.Y / CellHeight));
    }

    public static NavGrid FromTilemap(Transform transform, Tilemap tilemap)
    {
        var blocked = new bool[Math.Max(0, tilemap.Width * tilemap.Height)];
        for (int y = 0; y < tilemap.Height; y++)
        {
            for (int x = 0; x < tilemap.Width; x++)
                blocked[y * tilemap.Width + x] = tilemap.SolidTiles.Contains(tilemap.GetTile(x, y));
        }

        return new NavGrid(tilemap.Width, tilemap.Height,
            tilemap.TileWidth * transform.Scale.X, tilemap.TileHeight * transform.Scale.Y,
            transform.Position, blocked);
    }
}
