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

    /// <summary>
    /// Nome de uma entidade a perseguir continuamente — o inimigo que corre atrás do jogador,
    /// sem script. O World reaponta o destino pra posição dela a cada <see cref="RepathInterval"/>,
    /// então o alvo pode andar à vontade. Vazio (padrão) = o destino só vem de
    /// <see cref="SetTarget(Vector2)"/>, como sempre foi.
    /// </summary>
    public string Follow = "";

    /// <summary>Segundos entre recálculos do caminho quando <see cref="Follow"/> está ativo.
    /// Recalcular todo frame gasta A* à toa; muito espaçado faz o inimigo correr pra onde o
    /// jogador estava. 0.25s é o meio-termo pra alvo em velocidade de personagem.</summary>
    public float RepathInterval = 0.25f;

    /// <summary>Para de perseguir a mais de tantos pixels do alvo. 0 = persegue de qualquer
    /// distância. Evita a cena inteira de inimigos correndo atrás do jogador de uma vez.</summary>
    public float FollowRange;

    internal float RepathTimer;

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
