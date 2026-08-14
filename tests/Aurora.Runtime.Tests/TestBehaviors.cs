using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;

namespace Aurora.Runtime.Tests;

/// <summary>
/// Behavior que só anota o que o <see cref="World"/> mandou pra ele. A engine não expõe
/// os callbacks de colisão/dano de outro jeito — eles são entregues a Behaviors e mais
/// nada — então gravar as chamadas aqui é como os testes observam esses sistemas.
/// </summary>
public sealed class RecordingBehavior : Behavior
{
    public readonly List<Entity> CollisionsWith = [];
    public readonly List<CollisionInfo> CollisionInfos = [];
    public readonly List<Entity> TriggerEnters = [];
    public readonly List<Entity> TriggerExits = [];
    public readonly List<float> DamageTaken = [];
    public int DeathCount;
    public int DestroyCount;
    public int StartCount;
    public int UpdateCount;

    public override void Start() => StartCount++;

    public override void Update(float deltaTime) => UpdateCount++;

    public override void OnCollision(Entity other, CollisionInfo info)
    {
        CollisionsWith.Add(other);
        CollisionInfos.Add(info);
    }

    public override void OnTriggerEnter(Entity other) => TriggerEnters.Add(other);

    public override void OnTriggerExit(Entity other) => TriggerExits.Add(other);

    public override void OnDamaged(float amount, Entity? source) => DamageTaken.Add(amount);

    public override void OnDeath() => DeathCount++;

    public override void OnDestroy() => DestroyCount++;
}

/// <summary>Behavior que sempre explode no Update — usado pra provar que o World isola a falha.</summary>
public sealed class ThrowingBehavior : Behavior
{
    public override void Update(float deltaTime)
        => throw new InvalidOperationException("falha proposital de teste");
}
