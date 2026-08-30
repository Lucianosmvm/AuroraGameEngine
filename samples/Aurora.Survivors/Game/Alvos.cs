using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Survivors;

/// <summary>
/// Busca de alvo compartilhada pelas armas. Mira por ETIQUETA (<c>#inimigo</c>), não por nome de
/// script: assim toda arma já funciona com qualquer inimigo novo que você criar, desde que o
/// prefab dele tenha o componente Tags com "inimigo".
/// </summary>
public static class Alvos
{
    /// <summary>O inimigo vivo mais próximo de <paramref name="origem"/> dentro de
    /// <paramref name="alcance"/>. False quando não há nenhum — a arma simplesmente não atira.</summary>
    public static bool MaisProximo(World world, Vector2 origem, float alcance, string etiqueta,
        out Entity alvo, out Vector2 posicao)
    {
        alvo = default;
        posicao = default;

        float melhor = alcance * alcance;
        bool achou = false;

        foreach (var (entity, health) in world.Query<Health>())
        {
            if (health.IsDead || !Tags.Matches(entity, etiqueta))
                continue;

            if (entity.Get<Transform>() is not { } transform)
                continue;

            float distancia = Vector2.DistanceSquared(origem, transform.Position);
            if (distancia > melhor)
                continue;

            melhor = distancia;
            alvo = entity;
            posicao = transform.Position;
            achou = true;
        }

        return achou;
    }
}
