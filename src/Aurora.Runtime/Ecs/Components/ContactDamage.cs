using System.Numerics;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Machuca quem encostar, enquanto estiver encostado. Serve pro inimigo que dá dano de corpo,
/// pro espinho, pra poça de lava, pra zona de fogo.
///
/// <para>Funciona com collider sólido (empurra e machuca, tipo inimigo) e com trigger
/// <c>IsSolid: false</c> (atravessa e machuca, tipo espinho ou fogo) — nos dois casos o dano se
/// repete a cada <see cref="Interval"/> enquanto o contato durar. Quem controla a
/// invulnerabilidade depois do golpe é o <see cref="Health"/> do alvo
/// (<c>InvulnerabilityAfterHit</c>), não este componente.</para>
/// </summary>
public sealed class ContactDamage : Behavior
{
    /// <summary>Dano por aplicação.</summary>
    public float Damage = 10f;

    /// <summary>Segundos entre aplicações enquanto o contato dura. 0 = só uma vez por contato:
    /// o alvo precisa sair e voltar pra levar de novo.</summary>
    public float Interval = 1f;

    /// <summary>Filtro de alvo (ver <see cref="Tags.Matches"/>): vazio = qualquer entidade com
    /// <see cref="Health"/>; <c>#etiqueta</c> = só quem tem a etiqueta; qualquer outra coisa =
    /// prefixo do nome, sem diferenciar maiúsculas. Use "Player" num inimigo pra ele não
    /// machucar os outros inimigos ao esbarrar.</summary>
    public string TargetPrefix = "";

    /// <summary>Empurrão aplicado ao alvo, em pixels, na direção oposta a esta entidade. 0 = sem
    /// empurrão. É deslocamento direto de posição, não impulso físico.</summary>
    public float Knockback;

    /// <summary>Se esta entidade se destrói ao acertar. Ligue pra armadilha de uso único ou
    /// projétil artesanal; deixe desligado num inimigo.</summary>
    public bool DestroySelfOnHit;

    // Quem está encostado agora. Colisão sólida chama OnCollision todo frame, mas trigger só
    // avisa na ENTRADA e na SAÍDA — sem guardar quem está dentro, um espinho atravessável
    // machucaria uma vez só e o Interval nunca valeria pra ele.
    private readonly HashSet<int> _touching = [];

    // Quando cada alvo pode levar dano de novo. Por entidade, e não um timer só, porque quem
    // encosta em dois alvos ao mesmo tempo tem que machucar os dois no ritmo certo — um timer
    // global faria o segundo alvo perder a vez.
    private readonly Dictionary<int, float> _nextHitAt = new();
    private float _clock;

    public override void Update(float deltaTime)
    {
        _clock += deltaTime;

        if (_touching.Count == 0)
            return;

        // ToList: TryHit pode destruir esta entidade (DestroySelfOnHit) ou o alvo, e as duas
        // coisas mexem nos dicionários no meio da iteração.
        foreach (int id in _touching.ToList())
        {
            if (World?.IsAlive(id) != true)
            {
                _touching.Remove(id);
                _nextHitAt.Remove(id);
                continue;
            }

            TryHit(World.GetEntity(id));
        }
    }

    public override void OnCollision(Entity other, CollisionInfo info) => TryHit(other);

    public override void OnTriggerEnter(Entity other)
    {
        _touching.Add(other.Id);
        TryHit(other);
    }

    public override void OnTriggerExit(Entity other)
    {
        _touching.Remove(other.Id);
        _nextHitAt.Remove(other.Id);
    }

    private void TryHit(Entity target)
    {
        if (World is null || target.Get<Health>() is null)
            return;

        if (!Tags.Matches(target, TargetPrefix))
            return;

        if (_nextHitAt.TryGetValue(target.Id, out float allowedAt) && _clock < allowedAt)
            return;

        // Interval 0 = uma vez por contato: marca infinito, e só a saída do contato (ou a morte
        // do alvo) limpa a marca.
        _nextHitAt[target.Id] = Interval > 0f ? _clock + Interval : float.PositiveInfinity;

        World.Damage(target, Damage, Entity);
        ApplyKnockback(target);

        if (DestroySelfOnHit)
            Entity.Destroy();
    }

    private void ApplyKnockback(Entity target)
    {
        if (Knockback <= 0f)
            return;

        if (Get<Transform>() is not { } mine || target.Get<Transform>() is not { } theirs)
            return;

        var away = theirs.Position - mine.Position;
        if (away.LengthSquared() <= 0.0001f)
            return;

        theirs.Position += Vector2.Normalize(away) * Knockback;
    }
}
