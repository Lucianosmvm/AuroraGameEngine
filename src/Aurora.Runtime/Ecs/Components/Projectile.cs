using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>Ataque à distância: anda reto na direção de <see cref="Velocity"/>, causa dano em
/// quem tocar (via World.Damage) e se destrói — no toque ou depois de Life segundos. Precisa
/// de Transform + Collider com IsSolid=false (senão empurra fisicamente em vez de disparar
/// OnTriggerEnter). World é injetado automático (todo Behavior ganha, ver World.Add) — spawne
/// direto com World.CreateEntity + Add, sem precisar de nenhuma injeção manual:
/// <code>
/// var p = world.CreateEntity("Projectile");
/// p.Add(new Transform { Position = origin });
/// p.Add(new SpriteRenderer { Texture = texture });
/// p.Add(new Collider { Width = 8f, Height = 8f, IsSolid = false });
/// p.Add(new Projectile { Velocity = direction * speed, Damage = 20f, Source = shooterEntity });
/// </code></summary>
public sealed class Projectile : Behavior
{
    public float Life = 2f;
    public float Damage = 20f;

    /// <summary>Filtro de alvo (ver <see cref="Tags.Matches"/>): "" (padrão) acerta qualquer
    /// entidade com Health exceto a própria Source; <c>#etiqueta</c> só quem tem a etiqueta;
    /// qualquer outra coisa é prefixo do nome, sem diferenciar maiúsculas.</summary>
    public string TargetPrefix = "";

    /// <summary>Direção * velocidade — sete no spawn, não é float/int/bool/string então não
    /// aparece no Inspector (não faz sentido autorar isso numa cena estática mesmo).</summary>
    public Vector2 Velocity;

    /// <summary>Quem disparou — ignorado no primeiro toque (o projétil nasce na posição de
    /// quem atirou, senão acertaria a própria fonte no frame de spawn) e repassado pro
    /// World.Damage como causador. Null = sem fonte (ex: armadilha, torreta sem dono).</summary>
    public Entity? Source;

    public override void Update(float dt)
    {
        var transform = Get<Transform>();
        if (transform is not null)
            transform.Position += Velocity * dt;

        Life -= dt;
        if (Life <= 0f)
            Entity.Destroy();
    }

    public override void OnTriggerEnter(Entity other)
    {
        if (Source is { } source && other.Id == source.Id)
            return;

        if (Tags.Matches(other, TargetPrefix))
            World?.Damage(other, Damage, Source);

        Entity.Destroy();
    }
}
