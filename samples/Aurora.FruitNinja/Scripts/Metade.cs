using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;

namespace FruitNinja;

/// <summary>
/// Metade de fruta já cortada: só cai, gira e some. Não pode ser cortada de novo nem custa
/// vida ao sair da tela — por isso é um script separado da <see cref="Fruta"/> em vez de uma
/// flag dentro dela: quem procura alvo de corte varre <c>Query&lt;Fruta&gt;</c> e as metades
/// simplesmente não estão lá.
/// </summary>
[SceneScript]
public sealed class Metade : Behavior
{
    public float VelX;
    public float VelY;
    public float Giro = 3f;

    /// <summary>Começa a desaparecer quando falta este tanto pra sair da tela. Sumir no ar em
    /// vez de bater na borda esconde o corte do enquadramento.</summary>
    public float DistanciaDoFade = 260f;

    public override void Update(float deltaTime)
    {
        if (Get<Transform>() is not { } transform)
            return;

        float dt = deltaTime * (Partida.Atual?.EscalaTempo ?? 1f);

        VelY += Arena.Gravidade * dt;
        transform.Position += new Vector2(VelX, VelY) * dt;
        transform.Rotation += Giro * dt;

        if (transform.Position.Y >= Arena.LimiteDeSaida)
        {
            Entity.Destroy();
            return;
        }

        if (Get<SpriteRenderer>() is { } sprite)
        {
            float distancia = Arena.LimiteDeSaida - transform.Position.Y;
            sprite.Color = Color.White.WithAlpha(Math.Clamp(distancia / DistanciaDoFade, 0f, 1f));
        }
    }
}
