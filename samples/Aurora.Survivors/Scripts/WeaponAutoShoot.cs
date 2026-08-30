using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;

namespace Survivors;

/// <summary>
/// Arma automática: a cada intervalo, mira sozinha no inimigo mais próximo e dispara. É o molde
/// pra qualquer arma nova — copie o arquivo, troque o prefab e a forma de mirar.
///
/// <para>Dano, cadência, velocidade e quantidade de projéteis saem da <see cref="PlayerStats"/>,
/// então todo upgrade vale pra ela sem precisar mexer aqui.</para>
/// </summary>
[SceneScript]
public sealed class WeaponAutoShoot : Behavior
{
    /// <summary>Prefab do projétil (precisa ter Projectile + Collider com IsSolid falso).</summary>
    public string Prefab = "prefabs/tiro.json";

    /// <summary>Segundos entre disparos, antes do multiplicador de cadência.</summary>
    public float Interval = 0.75f;

    /// <summary>Dano base de um projétil, antes do multiplicador de dano.</summary>
    public float Damage = 12f;

    /// <summary>Alcance da mira automática em pixels. Fora disso, não atira — vale a pena manter
    /// perto da meia-largura da tela (640px em 1280x720), pra arma acertar o que aparece.</summary>
    public float Range = 620f;

    /// <summary>Ângulo em graus entre projéteis quando sai mais de um por disparo.</summary>
    public float SpreadDegrees = 14f;

    /// <summary>Quem é alvo: etiqueta (<c>#inimigo</c>) ou prefixo de nome. Ver Tags.Matches.</summary>
    public string TargetTag = "#inimigo";

    /// <summary>Distância do centro do jogador onde o projétil nasce.</summary>
    public float MuzzleDistance = 16f;

    private float _cooldown;

    public override void Update(float deltaTime)
    {
        if (World is null || Get<Transform>() is not { } transform)
            return;

        var stats = Get<PlayerStats>();
        float cadencia = MathF.Max(0.05f, stats?.FireRateMultiplier ?? 1f);

        _cooldown -= deltaTime * cadencia;
        if (_cooldown > 0f)
            return;

        if (!Alvos.MaisProximo(World, transform.Position, Range, TargetTag, out _, out var alvo))
            return;

        var direcao = alvo - transform.Position;
        if (direcao.LengthSquared() <= 0.0001f)
            return;

        direcao = Vector2.Normalize(direcao);
        _cooldown = Interval;

        int quantidade = Math.Max(1, stats?.ProjectileCount ?? 1);
        float anguloBase = MathF.Atan2(direcao.Y, direcao.X);
        float passo = SpreadDegrees * MathF.PI / 180f;

        // Leque centrado na mira: 1 projétil vai reto, 2 abrem meio passo pra cada lado, e assim
        // por diante — sem esse deslocamento o "projétil extra" sairia todo pro mesmo lado.
        float inicio = anguloBase - passo * (quantidade - 1) / 2f;

        for (int i = 0; i < quantidade; i++)
        {
            float angulo = inicio + passo * i;
            Disparar(new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)), transform.Position, stats);
        }
    }

    private void Disparar(Vector2 direcao, Vector2 origem, PlayerStats? stats)
    {
        var spawned = World?.Spawn(Prefab, origem + direcao * MuzzleDistance);
        if (spawned is not { } tiro)
            return;

        // Velocidade, dono e dano só existem em runtime — o prefab guarda o resto (sprite,
        // collider, tempo de vida).
        if (tiro.Get<Projectile>() is { } projectile)
        {
            projectile.Velocity = direcao * (stats?.ProjectileSpeed ?? 340f);
            projectile.Damage = Damage * (stats?.DamageMultiplier ?? 1f);
            projectile.Source = Entity;
            projectile.TargetPrefix = TargetTag;
        }

        if (tiro.Get<Transform>() is { } transform)
            transform.Rotation = MathF.Atan2(direcao.Y, direcao.X);
    }
}
