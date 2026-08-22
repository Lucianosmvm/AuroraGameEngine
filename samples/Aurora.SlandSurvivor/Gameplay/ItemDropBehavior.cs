using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Scenes;
using Aurora.SlandSurvivor.Worlds;

namespace Aurora.SlandSurvivor.Gameplay;

/// <summary>
/// Item no chão: cai, quica de leve, é atraído quando o jogador chega perto e some ao ser
/// pego (ou depois de alguns minutos, para o mundo não encher de coisa esquecida).
/// O desenho não é um SpriteRenderer — o jogo desenha o ícone do item direto do atlas em
/// <see cref="SurvivorGame.OnRender"/>, assim bloco caído e bloco no inventário são a mesma arte.
/// </summary>
[SceneScript]
public sealed class ItemDropBehavior : Behavior
{
    public const float PickupRadius = 13f;
    public const float MagnetRadius = 62f;
    public const float Size = 10f;

    public int Item;
    public int Count = 1;

    public TileWorld Tiles = null!;
    public SurvivorGame Game = null!;

    public float Gravity = 1250f;
    public float Lifetime = 360f;

    private Vector2 _velocity;
    private float _age;
    private float _pickupDelay = 0.25f;

    /// <summary>Fase da flutuação, para o item não balançar todo em sincronia com os vizinhos.</summary>
    public float Bob => MathF.Sin((_age + Entity.Id * 0.37f) * 3f) * 1.6f;

    public void Launch(Vector2 velocity) => _velocity = velocity;

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        if (transform is null || World is null)
            return;

        _age += deltaTime;
        _pickupDelay = MathF.Max(0f, _pickupDelay - deltaTime);

        if (_age > Lifetime)
        {
            Entity.Destroy();
            return;
        }

        var position = transform.Position;

        if (Game.PlayerTransform is { } player && _pickupDelay <= 0f)
        {
            var toPlayer = player.Position - position;
            float distance = toPlayer.Length();

            if (distance <= PickupRadius)
            {
                if (Game.Collect(Item, Count))
                {
                    Entity.Destroy();
                    return;
                }
            }
            else if (distance <= MagnetRadius)
            {
                // Ímã: quanto mais perto, mais forte — o item "salta" para a mão.
                float pull = 1f - distance / MagnetRadius;
                _velocity += toPlayer / distance * (900f * pull * deltaTime);
            }
        }

        bool floating = Tiles.IsLiquid(TileWorld.ToTile(position.X), TileWorld.ToTile(position.Y));
        _velocity.Y = MathF.Min(_velocity.Y + Gravity * (floating ? 0.15f : 1f) * deltaTime, floating ? 40f : 520f);
        _velocity.X *= 1f - MathF.Min(1f, 3.2f * deltaTime);

        transform.Position = position + _velocity * deltaTime;
    }

    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (info.Normal.Y < -0.5f)
        {
            _velocity.Y = _velocity.Y > 90f ? -_velocity.Y * 0.28f : 0f;   // quica se vier rápido
            _velocity.X *= 0.7f;
        }
        else if (MathF.Abs(info.Normal.X) > 0.5f)
        {
            _velocity.X *= -0.4f;
        }
    }
}
