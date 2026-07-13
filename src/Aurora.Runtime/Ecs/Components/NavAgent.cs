using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Move a entidade automaticamente até um destino, contornando tiles sólidos via A*
/// (ver <see cref="Aurora.Runtime.AI.AStarPathfinder"/>). Chame <see cref="SetTarget"/> —
/// o World cuida do resto a cada frame. Sem tilemap com SolidTiles na cena, anda reto
/// até o alvo (sem desviar de nada).
/// </summary>
public sealed class NavAgent : IComponent
{
    public float Speed = 100f;

    /// <summary>Distância pra considerar um waypoint "alcançado" e passar pro próximo.</summary>
    public float ArriveThreshold = 4f;

    public bool HasTarget { get; internal set; }
    public bool IsMoving => HasTarget;

    internal Vector2 Target;
    internal List<Vector2>? Path;
    internal int WaypointIndex;

    public void SetTarget(float x, float y)
    {
        Target = new Vector2(x, y);
        HasTarget = true;
        Path = null; // recalculado no próximo Update
        WaypointIndex = 0;
    }

    public void SetTarget(Vector2 target) => SetTarget(target.X, target.Y);

    public void Stop()
    {
        HasTarget = false;
        Path = null;
    }
}
