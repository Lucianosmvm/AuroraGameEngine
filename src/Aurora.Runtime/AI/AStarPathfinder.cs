using System.Numerics;

namespace Aurora.Runtime.AI;

/// <summary>A* clássico sobre um <see cref="NavGrid"/> — usado por <see cref="Ecs.Components.NavAgent"/>.</summary>
public static class AStarPathfinder
{
    private static readonly (int Dx, int Dy)[] Neighbors4 = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int Dx, int Dy)[] Neighbors8 =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// Caminho de <paramref name="startWorld"/> até <paramref name="endWorld"/>, em pontos de
    /// mundo (centro de cada célula). Null se o destino está bloqueado ou não há caminho.
    /// </summary>
    public static List<Vector2>? FindPath(NavGrid grid, Vector2 startWorld, Vector2 endWorld, bool allowDiagonal = true)
    {
        var start = grid.WorldToCell(startWorld);
        var end = grid.WorldToCell(endWorld);

        if (grid.IsBlocked(end))
            return null;

        if (start == end)
            return [grid.CellToWorld(end)];

        var open = new PriorityQueue<GridPos, float>();
        var cameFrom = new Dictionary<GridPos, GridPos>();
        var gScore = new Dictionary<GridPos, float> { [start] = 0f };
        var closed = new HashSet<GridPos>();
        var directions = allowDiagonal ? Neighbors8 : Neighbors4;

        open.Enqueue(start, Heuristic(start, end));

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (current == end)
                return ReconstructPath(grid, cameFrom, current);

            if (!closed.Add(current))
                continue;

            foreach (var (dx, dy) in directions)
            {
                var next = new GridPos(current.X + dx, current.Y + dy);
                if (grid.IsBlocked(next))
                    continue;

                // Não deixa "cortar quina": diagonal só passa se as duas células ortogonais vizinhas também estiverem livres.
                if (dx != 0 && dy != 0
                    && (grid.IsBlocked(new GridPos(current.X + dx, current.Y))
                        || grid.IsBlocked(new GridPos(current.X, current.Y + dy))))
                    continue;

                float stepCost = dx != 0 && dy != 0 ? 1.4142f : 1f;
                float tentativeG = gScore[current] + stepCost;

                if (gScore.TryGetValue(next, out float existing) && tentativeG >= existing)
                    continue;

                gScore[next] = tentativeG;
                cameFrom[next] = current;
                open.Enqueue(next, tentativeG + Heuristic(next, end));
            }
        }

        return null;
    }

    private static float Heuristic(GridPos a, GridPos b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static List<Vector2> ReconstructPath(NavGrid grid, Dictionary<GridPos, GridPos> cameFrom, GridPos current)
    {
        var path = new List<Vector2> { grid.CellToWorld(current) };
        while (cameFrom.TryGetValue(current, out var previous))
        {
            current = previous;
            path.Add(grid.CellToWorld(current));
        }
        path.Reverse();
        return path;
    }
}
