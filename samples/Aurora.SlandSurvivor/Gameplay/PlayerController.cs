using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;
using Aurora.SlandSurvivor.Items;
using Aurora.SlandSurvivor.Worlds;
using Silk.NET.Input;

namespace Aurora.SlandSurvivor.Gameplay;

/// <summary>
/// O jogador: andar/pular com colisão contra o tilemap, nadar, cavar, construir e bater.
///
/// <para>Convenção da engine: Y cresce para BAIXO. A resolução de colisão do
/// <see cref="World"/> roda depois de todos os Update, empurra quem ficou sobreposto para
/// fora e chama <see cref="OnCollision"/> — por isso <see cref="_grounded"/> é zerado no fim
/// do Update e remarcado lá.</para>
/// </summary>
[SceneScript]
public sealed class PlayerController : Behavior
{
    public const float HalfWidth = 6f;
    public const float HalfHeight = 11f;

    /// <summary>Alcance da picareta/mão, em tiles.</summary>
    public const float ReachTiles = 5.5f;

    public float MoveSpeed = 152f;
    public float Acceleration = 1500f;
    public float Friction = 1700f;
    public float AirControl = 0.72f;
    public float Gravity = 1500f;
    public float JumpSpeed = 430f;
    public float JumpCut = 0.45f;
    public float MaxFallSpeed = 620f;
    public float CoyoteTime = 0.10f;
    public float JumpBufferTime = 0.12f;

    /// <summary>Velocidade de queda a partir da qual o tombo machuca.</summary>
    public float SafeFallSpeed = 470f;

    public float SwingCooldown = 0.38f;

    // Preenchidos pelo jogo ao criar a entidade.
    public TileWorld Tiles = null!;
    public SurvivorGame Game = null!;
    public Inventory Backpack = null!;
    public Camera2D Cam = null!;

    /// <summary>Eixo horizontal externo (-1..1): joystick de tela, teste automatizado, cutscene.</summary>
    public float ExternalAxis;

    private Vector2 _velocity;
    private bool _grounded;
    private float _coyote;
    private float _jumpBuffer;
    private bool _jumpCutArmed;
    private float _peakFallSpeed;
    private float _swingTimer;
    private float _sinceDamage;
    private float _stepCooldown;
    private (int X, int Y)? _miningTile;

    public Vector2 Velocity => _velocity;
    public bool Grounded => _grounded;
    public bool InWater { get; private set; }

    /// <summary>Tile mirado agora (para o HUD desenhar o cursor), ou null se fora de alcance.</summary>
    public (int X, int Y)? AimTile { get; private set; }

    /// <summary>Progresso do golpe atual, 0–1. Usado só pelo desenho do braço/arma.</summary>
    public float SwingProgress => _swingTimer <= 0f ? 0f : 1f - _swingTimer / SwingCooldown;

    /// <summary>Direção do último golpe, para desenhar a arma do lado certo.</summary>
    public Vector2 SwingDirection { get; private set; } = Vector2.UnitX;

    public bool FacingLeft { get; private set; }

    public Vector2 Position => Get<Transform>()?.Position ?? Vector2.Zero;

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        var input = World?.Input;
        if (transform is null || input is null || Tiles is null)
            return;

        _swingTimer = MathF.Max(0f, _swingTimer - deltaTime);
        _stepCooldown = MathF.Max(0f, _stepCooldown - deltaTime);
        _sinceDamage += deltaTime;

        UpdateFluidState(transform.Position);

        float axis = Math.Clamp(input.AxisX + ExternalAxis, -1f, 1f);
        bool jumpPressed = input.WasKeyPressed(Key.Space) || input.WasKeyPressed(Key.W)
            || input.WasKeyPressed(Key.Up) || input.WasGamepadButtonPressed(ButtonName.A);
        bool jumpHeld = input.IsKeyDown(Key.Space) || input.IsKeyDown(Key.W)
            || input.IsKeyDown(Key.Up) || input.IsGamepadButtonDown(ButtonName.A);

        _coyote = _grounded ? CoyoteTime : MathF.Max(0f, _coyote - deltaTime);
        _jumpBuffer = jumpPressed ? JumpBufferTime : MathF.Max(0f, _jumpBuffer - deltaTime);

        // --- horizontal ------------------------------------------------------
        float speed = InWater ? MoveSpeed * 0.62f : MoveSpeed;
        float target = axis * speed;
        float rate = MathF.Abs(target) > 0.01f ? Acceleration : Friction;
        if (!_grounded)
            rate *= AirControl;

        _velocity.X = MoveTowards(_velocity.X, target, rate * deltaTime);
        if (MathF.Abs(axis) > 0.01f)
            FacingLeft = axis < 0f;

        // --- pulo / nado ------------------------------------------------------
        if (InWater)
        {
            // Na água o pulo vira braçada: pode repetir enquanto o botão estiver apertado.
            if (jumpHeld)
                _velocity.Y = MathF.Min(_velocity.Y, -150f);

            _velocity.Y = MathF.Min(_velocity.Y + Gravity * 0.28f * deltaTime, 128f);
            _jumpCutArmed = false;
            _peakFallSpeed = 0f;
        }
        else
        {
            if (_jumpBuffer > 0f && _coyote > 0f)
            {
                _velocity.Y = -JumpSpeed;
                _jumpBuffer = 0f;
                _coyote = 0f;
                _grounded = false;
                _jumpCutArmed = jumpHeld;
            }

            if (_jumpCutArmed && !jumpHeld && _velocity.Y < -JumpSpeed * JumpCut)
            {
                _velocity.Y = -JumpSpeed * JumpCut;
                _jumpCutArmed = false;
            }

            _velocity.Y = MathF.Min(_velocity.Y + Gravity * deltaTime, MaxFallSpeed);
            _peakFallSpeed = MathF.Max(_peakFallSpeed, _velocity.Y);
        }

        transform.Position += _velocity * deltaTime;

        // Degrau automático: subir um bloco andando, sem exigir pulo (como no gênero).
        if (_grounded && MathF.Abs(axis) > 0.1f && _stepCooldown <= 0f)
            TryStepUp(transform, MathF.Sign(axis));

        ClampToWorld(transform);

        if (Get<SpriteRenderer>() is { } sprite)
            sprite.FlipX = FacingLeft;

        HandleTools(input, transform.Position, deltaTime);
        UpdateSurvival(deltaTime);

        _grounded = false;      // o passo de colisão logo abaixo remarca se houver chão
    }

    public override void OnCollision(Entity other, CollisionInfo info)
    {
        if (info.Normal.Y < -0.5f)
        {
            if (!_grounded)
                ApplyFallDamage();

            _grounded = true;
            if (_velocity.Y > 0f)
                _velocity.Y = 0f;
        }
        else if (info.Normal.Y > 0.5f)
        {
            if (_velocity.Y < 0f)
                _velocity.Y = 0f;
        }
        else if (MathF.Abs(info.Normal.X) > 0.5f)
        {
            _velocity.X = 0f;
        }
    }

    public override void OnDamaged(float amount, Entity? source)
    {
        _sinceDamage = 0f;
        Game?.OnPlayerHurt(amount);
    }

    public override void OnDeath() => Game?.OnPlayerDeath();

    /// <summary>Empurrão (inimigo acertou, explosão). Some com o estado de chão para o knockback valer.</summary>
    public void Knockback(Vector2 direction, float force)
    {
        _velocity = direction * force + new Vector2(0f, -force * 0.45f);
        _grounded = false;
        _coyote = 0f;
    }

    public void Teleport(Vector2 position)
    {
        if (Get<Transform>() is { } transform)
            transform.Position = position;

        _velocity = Vector2.Zero;
        _peakFallSpeed = 0f;
        _miningTile = null;
    }

    public void RequestJump() => _jumpBuffer = JumpBufferTime;

    // ---------------------------------------------------------------------
    //  Ferramentas: cavar, construir, bater
    // ---------------------------------------------------------------------

    private void HandleTools(Aurora.Runtime.Input.InputManager input, Vector2 center, float deltaTime)
    {
        if (Game.UiBlocksWorldClicks)
        {
            AimTile = null;
            return;
        }

        var aim = Cam.ScreenToWorld(input.MousePosition);
        var (tx, ty) = Tiles.WorldToTile(aim);

        bool inReach = Vector2.Distance(aim, center) <= ReachTiles * TileWorld.TileSize;
        AimTile = inReach && Tiles.InBounds(tx, ty) ? (tx, ty) : null;

        int held = Backpack.SelectedItem;
        var heldDef = ItemDb.Get(held);

        // Botão esquerdo: cava o bloco mirado; sem bloco (ou com espada na mão), golpeia.
        if (input.IsMouseDown(MouseButton.Left))
        {
            bool minable = AimTile is { } tile && TileDb.Get(Tiles.Get(tile.X, tile.Y)) is { Hardness: >= 0f };

            if (minable && heldDef?.Kind != ItemKind.Sword)
                MineAimedTile(deltaTime, held);
            else
                Swing(center, aim);
        }
        else if (_miningTile is { } previous)
        {
            Tiles.ClearDamage(previous.X, previous.Y);      // soltou o botão: o bloco se recupera
            _miningTile = null;
        }

        if (input.WasMouseClicked(MouseButton.Right))
            UseSelected(center, aim);
    }

    private void MineAimedTile(float deltaTime, int heldItem)
    {
        if (AimTile is not { } tile)
            return;

        int tileId = Tiles.Get(tile.X, tile.Y);
        if (TileDb.Get(tileId) is not { } def)
            return;

        int power = ItemDb.PowerOf(heldItem);
        if (power < def.MinPower)
        {
            Game.Notify($"{def.Name}: precisa de uma picareta melhor.");
            return;
        }

        if (_miningTile is { } previous && (previous.X != tile.X || previous.Y != tile.Y))
            Tiles.ClearDamage(previous.X, previous.Y);

        _miningTile = tile;

        int drop = Tiles.MineTile(tile.X, tile.Y, deltaTime * power);
        if (Tiles.Get(tile.X, tile.Y) != TileId.Empty)
            return;                                         // ainda rachando

        Game.OnTileBroken(tile.X, tile.Y, tileId, drop);
        _miningTile = null;
    }

    private void UseSelected(Vector2 center, Vector2 aim)
    {
        int held = Backpack.SelectedItem;
        if (ItemDb.Get(held) is not { } def)
            return;

        if (def.Kind == ItemKind.Consumable)
        {
            var health = Get<Health>();
            if (health is null || health.Current >= health.Max)
                return;

            World?.Heal(Entity, def.Heal);
            Backpack.ConsumeSelected();
            Game.Notify($"{def.Name}: +{(int)def.Heal} de vida.");
            return;
        }

        if (def.PlaceTile < 0 || AimTile is not { } tile)
            return;

        PlaceBlock(tile.X, tile.Y, def, center);
    }

    /// <summary>
    /// Coloca o item selecionado no tile indicado, com as mesmas regras do botão direito
    /// (apoio, espaço livre, consumo do inventário). Devolve false se a regra barrar.
    /// </summary>
    public bool PlaceSelected(int tileX, int tileY)
        => ItemDb.Get(Backpack.SelectedItem) is { PlaceTile: >= 0 } def
           && PlaceBlock(tileX, tileY, def, Position);

    private bool PlaceBlock(int tx, int ty, ItemDef def, Vector2 center)
    {
        int existing = Tiles.Get(tx, ty);
        if (existing != TileId.Empty && existing != TileId.Water)
            return false;

        var placed = TileDb.Get(def.PlaceTile);
        bool solid = placed?.Solid == true;

        // Bloco sólido não pode nascer dentro do jogador.
        if (solid)
        {
            var tileCenter = TileWorld.TileCenter(tx, ty);
            if (MathF.Abs(tileCenter.X - center.X) < HalfWidth + TileWorld.TileSize * 0.5f
                && MathF.Abs(tileCenter.Y - center.Y) < HalfHeight + TileWorld.TileSize * 0.5f)
                return false;
        }

        // Precisa de apoio: vizinho sólido ou parede de fundo. Sem isso dava para desenhar
        // pontes flutuantes no meio do céu.
        bool supported = Tiles.IsSolid(tx - 1, ty) || Tiles.IsSolid(tx + 1, ty)
            || Tiles.IsSolid(tx, ty - 1) || Tiles.IsSolid(tx, ty + 1)
            || Tiles.GetWall(tx, ty) >= 0;

        if (!supported)
            return false;

        Tiles.SetTile(tx, ty, def.PlaceTile);
        Backpack.ConsumeSelected();
        Game.OnTilePlaced(tx, ty, def.PlaceTile);
        return true;
    }

    private void Swing(Vector2 center, Vector2 aim)
    {
        if (_swingTimer > 0f)
            return;

        _swingTimer = SwingCooldown;

        var direction = aim - center;
        SwingDirection = direction.LengthSquared() > 0.001f ? Vector2.Normalize(direction) : Vector2.UnitX;
        FacingLeft = SwingDirection.X < 0f;

        float damage = ItemDb.DamageOf(Backpack.SelectedItem);
        var hit = center + SwingDirection * 20f;
        Game.HitEnemies(hit, 24f, damage, SwingDirection, Entity);
    }

    // ---------------------------------------------------------------------
    //  Estado do corpo
    // ---------------------------------------------------------------------

    private void UpdateFluidState(Vector2 center)
    {
        int tile = Tiles.TileAt(center);
        InWater = tile == TileId.Water;

        if (tile == TileId.Lava)
            InWater = true;                                 // lava também segura o movimento
    }

    private void UpdateSurvival(float deltaTime)
    {
        var health = Get<Health>();
        if (health is null || World is null)
            return;

        // Lava queima continuamente; os i-frames do Health limitam a frequência real do dano.
        if (Tiles.TileAt(Position) == TileId.Lava)
            World.Damage(Entity, 14f, Entity);

        if (_sinceDamage > 6f && health.Current < health.Max)
            World.Heal(Entity, 2.5f * deltaTime);
    }

    private void ApplyFallDamage()
    {
        if (_peakFallSpeed <= SafeFallSpeed || InWater)
        {
            _peakFallSpeed = 0f;
            return;
        }

        float damage = (_peakFallSpeed - SafeFallSpeed) * 0.16f;
        _peakFallSpeed = 0f;
        World?.Damage(Entity, damage, Entity);
        Game.Notify($"Tombo feio: -{(int)damage} de vida.");
    }

    /// <summary>Sobe um bloco de altura ao esbarrar nele andando, se houver espaço em cima.</summary>
    private void TryStepUp(Transform transform, int direction)
    {
        var center = transform.Position;
        int frontX = TileWorld.ToTile(center.X + direction * (HalfWidth + 3f));
        int feetY = TileWorld.ToTile(center.Y + HalfHeight - 2f);

        bool blocked = Tiles.IsSolid(frontX, feetY);
        bool roomAbove = !Tiles.IsSolid(frontX, feetY - 1) && !Tiles.IsSolid(frontX, feetY - 2)
            && !Tiles.IsSolid(TileWorld.ToTile(center.X), feetY - 2);

        if (!blocked || !roomAbove)
            return;

        transform.Position = center with { Y = center.Y - TileWorld.TileSize };
        _stepCooldown = 0.08f;
        _grounded = true;
    }

    private void ClampToWorld(Transform transform)
    {
        float minX = HalfWidth;
        float maxX = Tiles.WorldWidth - HalfWidth;
        var position = transform.Position;

        if (position.X < minX || position.X > maxX)
        {
            position.X = Math.Clamp(position.X, minX, maxX);
            _velocity.X = 0f;
        }

        if (position.Y > Tiles.WorldHeight + 200f)
            position = TileWorld.TileCenter((int)Tiles.SpawnTile.X, (int)Tiles.SpawnTile.Y);

        transform.Position = position;
    }

    private static float MoveTowards(float current, float target, float maxDelta)
        => MathF.Abs(target - current) <= maxDelta ? target : current + MathF.Sign(target - current) * maxDelta;
}
