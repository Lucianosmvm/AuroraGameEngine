using System.Numerics;
using Aurora.Runtime.UI;
using Silk.NET.Input;

namespace Aurora.Runtime.Ecs.Components;

/// <summary>
/// Ataque por input: com um gatilho (tecla, clique, botão de toque) e um cooldown, instancia um
/// prefab a uma distância desta entidade, na direção da mira.
///
/// <para>Corpo-a-corpo ou à distância é decidido pelo PREFAB, não por aqui — o mesmo componente
/// serve os dois:</para>
/// <list type="bullet">
/// <item>Corte de espada: prefab com <see cref="Animator"/> + <see cref="Lifetime"/>
/// (<c>DestroyOnAnimationEnd</c>) + <see cref="FollowTarget"/> pra acompanhar quem atacou.</item>
/// <item>Tiro/magia: prefab com <see cref="Projectile"/> + <see cref="Collider"/> não-sólido.
/// Com <see cref="ProjectileSpeed"/> &gt; 0 a velocidade e o dono são preenchidos no spawn, que
/// são justamente os dois campos que não cabem num arquivo estático.</item>
/// </list>
///
/// <para>Pode ir em qualquer entidade, não só no jogador: uma torreta usa
/// <c>TriggerKey</c>/<c>TriggerMouse</c> vazios e é disparada por script ou por evento.</para>
/// </summary>
public sealed class AttackSpawner : Behavior
{
    /// <summary>Caminho do prefab instanciado a cada ataque (ex.: "prefabs/corte.json"). Vazio
    /// desliga o componente.</summary>
    public string Prefab = "";

    /// <summary>Segundos entre ataques.</summary>
    public float Cooldown = 0.35f;

    /// <summary>Distância do centro desta entidade até onde o prefab nasce, em pixels.</summary>
    public float Distance = 24f;

    /// <summary>"Facing" (padrão) mira pra onde o <see cref="TopDownController"/> está virado —
    /// funciona igual no celular, sem depender de cursor. "Mouse" mira o ponteiro. Sem
    /// TopDownController na entidade, Facing cai pra direita.</summary>
    public string AimMode = "Facing";

    /// <summary>Trava a mira em N direções iguais: 8 dá múltiplos de 45°, 4 dá só as retas,
    /// 0 (padrão) deixa livre. Serve pra casar com spritesheets que só têm algumas poses.</summary>
    public int DirectionSnap;

    /// <summary>Tecla que dispara, pelo nome do enum (ex.: "Space", "E", "J"). Vazio = nenhuma.</summary>
    public string TriggerKey = "";

    /// <summary>Dispara no clique esquerdo do mouse.</summary>
    public bool TriggerMouse;

    /// <summary>Tela de UI e nome do <c>UiButton</c> que dispara — o botão de ataque no celular.
    /// Vazio = nenhum.</summary>
    public string TriggerUiScreen = "";
    public string TriggerUiButton = "";

    /// <summary>Gira o prefab pra apontar na direção do ataque. Desligue se a arte já é
    /// omnidirecional (uma explosão, um clarão).</summary>
    public bool RotateSpawn = true;

    /// <summary>Se o prefab tiver <see cref="FollowTarget"/>, aponta pra esta entidade e ajusta o
    /// Offset pra direção do golpe. É o que faz o corte acompanhar o personagem enquanto ele anda
    /// durante a animação, sem precisar de um script de efeito.</summary>
    public bool AttachToAttacker = true;

    /// <summary>Se o prefab tiver <see cref="Projectile"/>, velocidade em pixels/s na direção da
    /// mira, e Source = esta entidade (pra não acertar quem atirou). 0 = não mexe no
    /// projétil.</summary>
    public float ProjectileSpeed;

    /// <summary>Segundos que faltam pro próximo ataque ficar disponível.</summary>
    public float CooldownRemaining { get; private set; }

    public bool IsReady => CooldownRemaining <= 0f;

    public override void Update(float deltaTime)
    {
        CooldownRemaining = MathF.Max(0f, CooldownRemaining - deltaTime);

        if (IsReady && TriggerPressed())
            Attack();
    }

    /// <summary>Ataca agora se o cooldown deixar, ignorando os gatilhos de input. É por aqui que
    /// um script, um evento ou uma IA de inimigo mandam atacar.</summary>
    public bool Attack()
    {
        if (!IsReady || Prefab.Length == 0 || World is null || Get<Transform>() is not { } transform)
            return false;

        var direction = Snap(Aim(transform.Position));
        if (direction.LengthSquared() <= 0.0001f)
            return false;

        var offset = direction * Distance;
        var spawned = World.Spawn(Prefab, transform.Position + offset);
        if (spawned is not { } attack)
            return false;

        float angle = MathF.Atan2(direction.Y, direction.X);

        if (RotateSpawn && attack.Get<Transform>() is { } attackTransform)
            attackTransform.Rotation = angle;

        if (AttachToAttacker && attack.Get<FollowTarget>() is { } follow)
        {
            follow.TargetName = Entity.Name;
            follow.Offset = offset;
        }

        if (ProjectileSpeed > 0f && attack.Get<Projectile>() is { } projectile)
        {
            projectile.Velocity = direction * ProjectileSpeed;
            projectile.Source = Entity;
        }

        CooldownRemaining = Cooldown;
        return true;
    }

    private bool TriggerPressed()
    {
        var input = World?.Input;

        if (TriggerKey.Length > 0 && input is not null
            && Enum.TryParse<Key>(TriggerKey, ignoreCase: true, out var key)
            && input.WasKeyPressed(key))
            return true;

        if (TriggerMouse && (input?.WasMouseClicked(MouseButton.Left) ?? false))
            return true;

        return TriggerUiButton.Length > 0
            && World?.UI?.Find<UiButton>(TriggerUiScreen, TriggerUiButton) is { Clicked: true };
    }

    private Vector2 Aim(Vector2 origin)
    {
        if (string.Equals(AimMode, "Mouse", StringComparison.OrdinalIgnoreCase))
        {
            // Sem câmera não dá pra converter pixel de tela em ponto de mundo; cair pro Facing é
            // melhor que atacar numa direção aleatória.
            if (World?.Camera is { } camera && World.Input is { } input)
            {
                var delta = camera.ScreenToWorld(input.MousePosition) - origin;
                if (delta.LengthSquared() > 0.0001f)
                    return Vector2.Normalize(delta);
            }
        }

        return Get<TopDownController>()?.Facing ?? new Vector2(1f, 0f);
    }

    private Vector2 Snap(Vector2 direction)
    {
        if (DirectionSnap <= 0 || direction.LengthSquared() <= 0.0001f)
            return direction;

        float step = MathF.Tau / DirectionSnap;
        float angle = MathF.Round(MathF.Atan2(direction.Y, direction.X) / step) * step;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}
