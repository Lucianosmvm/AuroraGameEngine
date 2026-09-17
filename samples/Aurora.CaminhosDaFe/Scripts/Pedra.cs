using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace CaminhosDaFe;

/// <summary>
/// A pedra da funda. Não é o <c>Projectile</c> nativo porque aquele some ao tocar QUALQUER
/// gatilho — aqui ela atravessa ovelha e NPC (ninguém quer acertar o próprio rebanho), só para
/// em inimigo ou em tile sólido, e empurra quem acerta.
/// </summary>
[SceneScript]
public sealed class Pedra : Behavior
{
    /// <summary>Segundos no ar antes de cair — é o que limita o alcance.</summary>
    public float Duracao = 0.7f;

    /// <summary>Pixels que o alvo recua com o impacto.</summary>
    public float Empurrao = 10f;

    public string Alvo = "#inimigo";

    public Vector2 Velocidade;
    public float Dano = 10f;
    public Entity? Fonte;

    public override void Update(float deltaTime)
    {
        if (Get<Transform>() is not { } transform)
            return;

        transform.Position += Velocidade * deltaTime;
        transform.Rotation += deltaTime * 14f;

        Duracao -= deltaTime;
        if (Duracao <= 0f)
            Entity.Destroy();
    }

    public override void OnTriggerEnter(Entity other)
    {
        if (World is null || (Fonte is { } fonte && other.Id == fonte.Id) || !Tags.Matches(other, Alvo))
            return;

        World.Damage(other, Dano, Fonte);

        if (other.Get<Transform>() is { } alvo && Velocidade.LengthSquared() > 0f)
            alvo.Position += Vector2.Normalize(Velocidade) * Empurrao;

        Entity.Destroy();
    }

    /// <summary>Tile sólido (rocha, cerca, tronco) avisa por colisão — a pedra quica e some.</summary>
    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (other.Has<Tilemap>())
            Entity.Destroy();
    }
}
