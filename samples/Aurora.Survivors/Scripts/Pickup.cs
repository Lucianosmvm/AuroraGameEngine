using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

/// <summary>
/// Coisa largada no chão que voa pro jogador quando ele chega perto: gema de XP e moeda usam o
/// mesmo script, mudando só o <see cref="Kind"/>. O raio do ímã vem da ficha do jogador
/// (<see cref="PlayerStats.PickupRadius"/>), então o upgrade de ímã vale pros dois de graça.
/// </summary>
[SceneScript]
public sealed class Pickup : Behavior
{
    /// <summary><c>Xp</c> soma na barra de experiência; <c>Moeda</c> entra no inventário (e no
    /// save, pra gastar na loja entre partidas). Qualquer outro valor não faz nada.</summary>
    public string Kind = "Xp";

    /// <summary>Quanto vale: pontos de XP, ou quantas moedas.</summary>
    public float Value = 1f;

    /// <summary>Velocidade em pixels/s com que voa pro jogador depois de atraído.</summary>
    public float Speed = 320f;

    /// <summary>Distância em que é considerado coletado.</summary>
    public float CollectRadius = 14f;

    public string TargetName = "Player";

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        if (!World.TryFind(TargetName, out var jogador) || jogador.Get<Transform>() is not { } destino)
            return;

        var delta = destino.Position - transform.Position;
        float distancia = delta.Length();

        if (distancia <= CollectRadius)
        {
            Coletar(jogador);
            return;
        }

        float raio = jogador.Get<PlayerStats>()?.PickupRadius ?? 70f;
        if (distancia > raio)
            return;

        // Acelera conforme chega perto: o "puxão" fica com cara de ímã em vez de arrasto linear.
        float atracao = Speed * (1f + (1f - distancia / raio));
        transform.Position += delta / distancia * atracao * deltaTime;
    }

    private void Coletar(Entity jogador)
    {
        if (Kind.Equals("Moeda", StringComparison.OrdinalIgnoreCase))
        {
            World?.Inventory?.Add("Moeda", Math.Max(1, (int)Value));
        }
        else
        {
            float multiplicador = jogador.Get<PlayerStats>()?.XpMultiplier ?? 1f;
            World?.State?.AddVariable("Xp", Value * multiplicador);
        }

        Entity.Destroy();
    }
}
