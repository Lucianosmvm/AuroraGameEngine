using Aurora.Runtime.Ecs;
using Aurora.Runtime.Scenes;

namespace Survivors;

/// <summary>
/// Ficha de atributos do jogador — a fonte da verdade que TUDO no jogo lê: as armas leem dano e
/// cadência, o <see cref="PlayerRunner"/> copia a velocidade pro TopDownController, os coletáveis
/// leem o raio do ímã.
///
/// <para>É por isso que upgrade de level up (<see cref="UpgradeCatalog"/>) e melhoria permanente
/// da loja (<see cref="MetaShop"/>) só mexem aqui: nenhum dos dois precisa conhecer arma,
/// inimigo ou HUD. Pra criar um atributo novo, adicione o campo aqui e leia onde ele importa —
/// campo público float/int/bool/string já aparece sozinho no Inspector do editor.</para>
/// </summary>
[SceneScript]
public sealed class PlayerStats : Behavior
{
    /// <summary>Vida máxima. Aplicada ao componente Health no Start do <see cref="PlayerRunner"/>.</summary>
    public float MaxHealth = 100f;

    /// <summary>Pixels por segundo — copiado pro TopDownController a cada frame.</summary>
    public float MoveSpeed = 145f;

    /// <summary>Multiplica o dano de toda arma (1 = dano base da arma).</summary>
    public float DamageMultiplier = 1f;

    /// <summary>Multiplica a cadência de tiro (2 = atira duas vezes mais rápido).</summary>
    public float FireRateMultiplier = 1f;

    /// <summary>Velocidade do projétil em pixels/s.</summary>
    public float ProjectileSpeed = 340f;

    /// <summary>Quantos projéteis saem por disparo (leque).</summary>
    public int ProjectileCount = 1;

    /// <summary>Raio em que gema/moeda começam a voar pro jogador.</summary>
    public float PickupRadius = 75f;

    /// <summary>Fração do dano recebido que é anulada (0 = nenhuma, 0.5 = metade). O
    /// <see cref="PlayerRunner"/> limita em 0,8 pra não existir jogador imortal.</summary>
    public float Armor;

    /// <summary>Multiplica o XP de cada gema coletada.</summary>
    public float XpMultiplier = 1f;

    /// <summary>Vida regenerada por segundo.</summary>
    public float RegenPerSecond;

    /// <summary>Quantas lâminas orbitam o jogador (0 = arma desligada). Ver <see cref="OrbitBlade"/>.</summary>
    public int OrbitBlades;

    /// <summary>Dano base de cada lâmina orbital, antes do <see cref="DamageMultiplier"/>.</summary>
    public float OrbitDamage = 9f;
}
