namespace Aurora.Runtime.Ecs.Components;

/// <summary>Vida de uma entidade. Não mexa em <see cref="Current"/> direto — use
/// <see cref="World.Damage"/>/<see cref="World.Heal"/>, senão OnDamaged/OnDeath dos
/// Behaviors (e a morte automática) não disparam.</summary>
public sealed class Health : IComponent
{
    public float Max = 100f;
    public float Current = 100f;

    /// <summary>Segundos de invencibilidade após tomar dano (0 = sem i-frames).</summary>
    public float InvulnerabilityAfterHit;

    /// <summary>True = ignora todo dano (liga/desliga em código — cutscene, escudo, etc.).</summary>
    public bool Invulnerable;

    /// <summary>True (padrão) = Entity.Destroy() automático ao chegar a 0. Desliga se o jogo
    /// quer tocar animação de morte antes — chama Entity.Destroy() manual no OnDeath.</summary>
    public bool DestroyOnDeath = true;

    public bool IsDead => Current <= 0f;

    // Timer de i-frames, decrementado pelo World a cada frame — não serializado na cena.
    internal float InvulnerabilityTimer;
}
