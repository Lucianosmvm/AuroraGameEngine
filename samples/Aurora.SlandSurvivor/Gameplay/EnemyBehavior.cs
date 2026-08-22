using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Aurora.SlandSurvivor.Worlds;

namespace Aurora.SlandSurvivor.Gameplay;

public enum EnemyKind { Slime, Zombie, Bat }

/// <summary>
/// Inimigo comum: persegue o jogador, machuca no contato e larga item ao morrer.
/// Um script só para os três tipos — o que muda são os números e o modo de locomoção
/// (pulo, caminhada ou voo), não a lógica.
/// </summary>
[SceneScript]
public sealed class EnemyBehavior : Behavior
{
    public EnemyKind Kind = EnemyKind.Slime;

    public TileWorld Tiles = null!;
    public SurvivorGame Game = null!;

    public float Speed = 54f;
    public float Gravity = 1450f;
    public float JumpSpeed = 320f;
    public float ContactDamage = 14f;
    public float KnockbackForce = 150f;
    public bool Flying;

    /// <summary>Distância (px) a partir da qual o inimigo some — evita acumular bicho no mundo.</summary>
    public float DespawnDistance = 1500f;

    public int LootItem = Items.ItemIds.Gel;
    public int LootMin = 1;
    public int LootMax = 3;

    private Vector2 _velocity;
    private bool _grounded;
    private bool _blocked;
    private float _hopTimer;
    private float _wobble;
    private float _age;

    public Vector2 Velocity => _velocity;

    public override void Start() => _hopTimer = 0.4f + Noise.Hash(Entity.Id, 3, 7) * 0.8f;

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        if (transform is null || World is null || Game.PlayerTransform is not { } player)
            return;

        _age += deltaTime;
        _wobble += deltaTime;

        var toPlayer = player.Position - transform.Position;
        float distance = toPlayer.Length();

        if (ShouldDespawn(distance))
        {
            Entity.Destroy();
            return;
        }

        if (Flying)
            MoveFlying(transform, toPlayer, distance, deltaTime);
        else
            MoveOnGround(transform, toPlayer, deltaTime);

        transform.Position += _velocity * deltaTime;

        if (Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = _velocity.X < 0f;

        TouchPlayer(transform.Position, player.Position);

        _grounded = false;
        _blocked = false;
    }

    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (info.Normal.Y < -0.5f)
        {
            _grounded = true;
            if (_velocity.Y > 0f)
                _velocity.Y = 0f;
        }
        else if (info.Normal.Y > 0.5f && _velocity.Y < 0f)
        {
            _velocity.Y = 0f;
        }
        else if (MathF.Abs(info.Normal.X) > 0.5f)
        {
            _blocked = true;
            _velocity.X = 0f;
        }
    }

    public override void OnDamaged(float amount, Entity? source) => Game.SpawnHitEffect(Get<Transform>()?.Position ?? Vector2.Zero);

    public override void OnDeath()
    {
        var position = Get<Transform>()?.Position ?? Vector2.Zero;
        int count = LootMin + (int)(Noise.Hash(Entity.Id, (int)position.X, (int)position.Y) * (LootMax - LootMin + 1));
        count = Math.Clamp(count, LootMin, LootMax);

        if (LootItem >= 0 && count > 0)
            Game.SpawnDrop(position, LootItem, count);

        Game.SpawnHitEffect(position);
    }

    /// <summary>Empurrão do golpe do jogador.</summary>
    public void Knockback(Vector2 direction, float force)
    {
        _velocity = direction * force + new Vector2(0f, -force * 0.4f);
        _grounded = false;
    }

    // ---------------------------------------------------------------------

    private bool ShouldDespawn(float distance)
    {
        if (distance > DespawnDistance)
            return true;

        // Bicho de superfície evapora ao amanhecer, desde que não esteja na cara do jogador.
        return Kind != EnemyKind.Bat && Game.IsDaytime && distance > 340f && _age > 3f;
    }

    private void MoveOnGround(Transform transform, Vector2 toPlayer, float deltaTime)
    {
        float direction = MathF.Sign(toPlayer.X);
        if (direction == 0f)
            direction = 1f;

        if (Kind == EnemyKind.Slime)
        {
            // Gosma anda aos pulos: só recebe impulso quando encosta no chão.
            _hopTimer -= deltaTime;
            if (_grounded && _hopTimer <= 0f)
            {
                _velocity = new Vector2(direction * Speed, -JumpSpeed);
                _hopTimer = 0.9f + Noise.Hash(Entity.Id, (int)_age, 11) * 0.7f;
            }
            else if (_grounded)
            {
                _velocity.X *= 0.86f;                      // atrito ao aterrissar
            }
        }
        else
        {
            _velocity.X = direction * Speed;

            // Parede na frente ou buraco no caminho: pula.
            if (_grounded && (_blocked || IsGapAhead(transform.Position, direction)))
                _velocity.Y = -JumpSpeed;
        }

        _velocity.Y = MathF.Min(_velocity.Y + Gravity * deltaTime, 620f);
    }

    private void MoveFlying(Transform transform, Vector2 toPlayer, float distance, float deltaTime)
    {
        var direction = distance > 0.01f ? toPlayer / distance : Vector2.UnitX;

        // Voo em zigue-zague: um morcego que vem em linha reta é fácil demais de acertar.
        direction.Y += MathF.Sin(_wobble * 4.5f) * 0.55f;
        direction.X += MathF.Cos(_wobble * 2.3f) * 0.25f;

        var desired = Vector2.Normalize(direction) * Speed;
        _velocity = Vector2.Lerp(_velocity, desired, 1f - MathF.Exp(-4f * deltaTime));

        // Encostou na rocha: sobe um pouco para contornar em vez de raspar na parede.
        if (_blocked)
            _velocity.Y -= 260f * deltaTime;
    }

    /// <summary>Buraco logo à frente, na altura dos pés (evita andar para dentro do abismo).</summary>
    private bool IsGapAhead(Vector2 center, float direction)
    {
        int x = TileWorld.ToTile(center.X + direction * 14f);
        int y = TileWorld.ToTile(center.Y + 14f);
        return !Tiles.IsSolid(x, y) && !Tiles.IsSolid(x, y + 1);
    }

    private void TouchPlayer(Vector2 position, Vector2 playerPosition)
    {
        var delta = playerPosition - position;
        if (MathF.Abs(delta.X) > 16f || MathF.Abs(delta.Y) > 20f)
            return;

        Game.DamagePlayer(ContactDamage, Vector2.Normalize(delta with { Y = delta.Y - 6f }), KnockbackForce, Entity);
    }
}
