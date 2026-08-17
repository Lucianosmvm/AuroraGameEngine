using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.UI;
using Silk.NET.Input;

namespace AuroraFarm;

/// <summary>
/// Movimento (joystick de toque no celular, WASD/gamepad no desktop, igual
/// docs/TUTORIAL-JOGO-ANDROID.md) + um botão de ação contextual: capina grama, planta na
/// terra arada, colhe quando a plantação termina de crescer. Sem inventário de sementes —
/// plantar é de graça nesta base; adicione custo/`InventoryManager` quando continuar o jogo.
/// </summary>
public sealed class PlayerFarmer : Behavior
{
    public const int GrassTile = 0;
    public const int TilledTile = 1;

    public float Speed = 200f;
    public string TilemapEntityName = "Farmland";
    public string HudScreenId = "Hud";
    public string StickName = "MoveStick";
    public string ActionButtonName = "ActionButton";

    /// <summary>Tempo (segundos) até a plantação ficar pronta pra colher.</summary>
    public float GrowSeconds = 8f;

    /// <summary>Ouro ganho por colheita.</summary>
    public int HarvestPayout = 5;

    public Texture2D SeedlingTexture = null!;
    public Texture2D GrownTexture = null!;

    // Direção pra onde o jogador está virado — decide qual tile a ação mira. Começa virado
    // pra baixo (convenção comum de jogo top-down).
    private Vector2 _facing = new(0f, 1f);

    // Uma plantação por tile — dono é o próprio jogador porque só ele planta/colhe; não
    // precisa de um sistema separado pra isso numa base de um jogador só.
    private readonly Dictionary<(int X, int Y), Entity> _crops = new();

    public override void Update(float deltaTime)
    {
        var transform = Get<Transform>();
        if (World is null || transform is null)
            return;

        Move(transform, deltaTime);

        bool actionPressed = (World.UI?.Find<UiButton>(HudScreenId, ActionButtonName)?.Clicked ?? false)
            || (World.Input?.WasKeyPressed(Key.Space) ?? false);

        if (actionPressed)
            DoAction(transform);
    }

    private void Move(Transform transform, float deltaTime)
    {
        // Joystick manda quando tocado; sem toque, cai pro teclado/gamepad (AxisX/AxisY já
        // combina os dois) — o mesmo script funciona no Play do editor e no celular.
        var stick = World!.UI?.Find<UiJoystick>(HudScreenId, StickName);
        var move = stick?.Value ?? Vector2.Zero;

        if (move.LengthSquared() <= 0.0001f && World.Input is { } input)
            move = new Vector2(input.AxisX, input.AxisY);

        if (move.LengthSquared() <= 0.0001f)
            return;

        if (move.LengthSquared() > 1f)
            move = Vector2.Normalize(move);

        transform.Position += move * Speed * deltaTime;
        _facing = move;

        var sprite = Get<SpriteRenderer>();
        if (sprite is not null)
            sprite.FlipX = move.X < 0f;
    }

    private void DoAction(Transform transform)
    {
        if (!World!.TryFind(TilemapEntityName, out var mapEntity))
            return;

        var tilemap = mapEntity.Get<Tilemap>();
        var mapTransform = mapEntity.Get<Transform>();
        if (tilemap is null || mapTransform is null)
            return;

        var direction = _facing.LengthSquared() > 0.01f ? Vector2.Normalize(_facing) : new Vector2(0f, 1f);
        var targetWorld = transform.Position + direction * tilemap.TileWidth;

        int tx = (int)MathF.Floor((targetWorld.X - mapTransform.Position.X) / tilemap.TileWidth);
        int ty = (int)MathF.Floor((targetWorld.Y - mapTransform.Position.Y) / tilemap.TileHeight);
        if (tx < 0 || ty < 0 || tx >= tilemap.Width || ty >= tilemap.Height)
            return;

        var key = (tx, ty);
        if (_crops.TryGetValue(key, out var cropEntity) && cropEntity.IsAlive)
        {
            if (cropEntity.Get<CropPlot>() is { Ready: true })
            {
                World.State?.AddVariable("Gold", HarvestPayout);
                cropEntity.Destroy();
                _crops.Remove(key);
            }
            return; // ainda crescendo — nada a fazer
        }

        int tile = tilemap.GetTile(tx, ty);
        if (tile == GrassTile)
        {
            tilemap.SetTile(tx, ty, TilledTile);
        }
        else if (tile == TilledTile)
        {
            var center = mapTransform.Position + new Vector2(
                (tx + 0.5f) * tilemap.TileWidth, (ty + 0.5f) * tilemap.TileHeight);

            var crop = World.CreateEntity("Crop");
            crop.Add(new Transform(center));
            crop.Add(new SpriteRenderer(SeedlingTexture, layer: 3) { Size = new Vector2(36f, 36f) });
            crop.Add(new CropPlot { GrowSeconds = GrowSeconds, GrownTexture = GrownTexture });
            _crops[key] = crop;
        }
    }
}
