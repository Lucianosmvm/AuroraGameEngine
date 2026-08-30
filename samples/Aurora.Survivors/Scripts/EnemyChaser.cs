using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

/// <summary>
/// Inimigo padrão: anda reto na direção do jogador e, ao morrer, larga a gema de XP (e às vezes
/// uma moeda). O dano por encostar é do componente nativo ContactDamage, no prefab.
///
/// <para>Movimento reto, não pathfinding: a arena não tem parede, e um NavAgent por bicho em
/// centenas de inimigos custaria caro à toa. Inimigo que precise desviar de parede é só trocar
/// este script por NavAgent + SetTarget (ver docs/REFERENCIA-SCRIPTS-RPG.md).</para>
/// </summary>
[SceneScript]
public sealed class EnemyChaser : Behavior
{
    public float Speed = 58f;
    public string TargetName = "Player";

    /// <summary>XP que a gema largada vale.</summary>
    public float Xp = 1f;

    /// <summary>Chance de 0 a 1 de largar uma moeda além da gema.</summary>
    public float CoinChance = 0.07f;

    public string XpPrefab = "prefabs/gema.json";
    public string CoinPrefab = "prefabs/moeda.json";

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        if (!World.TryFind(TargetName, out var alvo) || alvo.Get<Transform>() is not { } destino)
            return;

        var direcao = destino.Position - transform.Position;
        if (direcao.LengthSquared() <= 1f)
            return;

        direcao = Vector2.Normalize(direcao);
        transform.Position += direcao * Speed * (Get<Status>()?.SpeedMultiplier ?? 1f) * deltaTime;

        if (Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = direcao.X < 0f;
    }

    /// <summary>Morte: solta o loot e conta a morte. Roda ANTES da entidade ser destruída (ver
    /// World.Damage), então a posição ainda é válida — é onde o drop cai.</summary>
    public override void OnDeath()
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        World.State?.AddVariable("Kills", 1f);

        if (World.Spawn(XpPrefab, transform.Position) is { } gema
            && gema.Get<Pickup>() is { } pickup)
        {
            pickup.Value = Xp;
        }

        if (CoinChance > 0f && Random.Shared.NextSingle() < CoinChance)
            World.Spawn(CoinPrefab, transform.Position);
    }
}
