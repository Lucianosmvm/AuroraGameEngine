using System.Numerics;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Input;
using Aurora.SlandSurvivor.Items;
using Aurora.SlandSurvivor.Worlds;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Aurora.SlandSurvivor.UI;

/// <summary>
/// Interface do jogo, desenhada em coordenadas de tela (1280x720 de projeto, ver
/// <c>DesignResolution</c>): vida, barra rápida, mochila, fabricação, minimapa e avisos.
///
/// <para>Tudo aqui é retângulo + fonte + ícone do atlas; nada de sistema de UI retido. Os
/// painéis que aceitam clique marcam <see cref="BlocksWorldClicks"/>, e o jogador para de
/// cavar enquanto o ponteiro está sobre eles — senão cada clique em "fabricar" abriria um
/// buraco no chão atrás do painel.</para>
/// </summary>
public sealed class Hud
{
    private const float SlotSize = 46f;
    private const float SlotGap = 6f;
    private const float MinimapWidth = 240f;
    private const float MinimapHeight = 150f;
    private const int MinimapTilesX = 160;
    private const int MinimapTilesY = 100;

    private static readonly Color Panel = new(0.06f, 0.07f, 0.10f, 0.86f);
    private static readonly Color PanelLine = new(0.42f, 0.46f, 0.56f, 0.9f);
    private static readonly Color SlotBack = new(0.14f, 0.16f, 0.21f, 0.88f);
    private static readonly Color SlotLine = new(0.32f, 0.35f, 0.43f, 0.95f);
    private static readonly Color Selected = new(1f, 0.86f, 0.42f, 1f);

    private readonly GL _gl;
    private readonly Font _font;
    private readonly Font _small;
    private readonly Texture2D _tileset;
    private readonly SurvivorGame _game;

    private Texture2D? _minimap;
    private float _minimapTimer;
    private string _message = "";
    private float _messageTimer;

    public Hud(GL gl, Font font, Font small, Texture2D tileset, SurvivorGame game)
    {
        _gl = gl;
        _font = font;
        _small = small;
        _tileset = tileset;
        _game = game;
    }

    public bool ShowInventory { get; set; }
    public bool ShowCrafting { get; set; }
    public bool ShowMinimap { get; set; } = true;
    public bool ShowHelp { get; set; } = true;

    /// <summary>True quando o ponteiro está sobre um painel aberto (o mundo ignora o clique).</summary>
    public bool BlocksWorldClicks { get; private set; }

    public void Notify(string message)
    {
        _message = message;
        _messageTimer = 3.2f;
    }

    public void Update(InputManager input, Inventory backpack, float deltaTime)
    {
        _messageTimer = MathF.Max(0f, _messageTimer - deltaTime);
        _minimapTimer -= deltaTime;

        if (input.WasKeyPressed(Key.Tab) || input.WasKeyPressed(Key.E))
            ShowInventory = !ShowInventory;
        if (input.WasKeyPressed(Key.C))
            ShowCrafting = !ShowCrafting;
        if (input.WasKeyPressed(Key.M))
            ShowMinimap = !ShowMinimap;
        if (input.WasKeyPressed(Key.H))
            ShowHelp = !ShowHelp;

        var mouse = input.MousePosition;
        BlocksWorldClicks = (ShowCrafting && CraftingBounds().Contains(mouse))
            || (ShowInventory && InventoryBounds().Contains(mouse))
            || HotbarBounds().Contains(mouse);

        if (input.WasMouseClicked())
            HandleClick(mouse, backpack);
    }

    private void HandleClick(Vector2 mouse, Inventory backpack)
    {
        if (ShowCrafting)
        {
            for (int i = 0; i < Recipes.All.Length; i++)
            {
                if (!RecipeBounds(i).Contains(mouse))
                    continue;

                var recipe = Recipes.All[i];
                if (Recipes.Craft(backpack, recipe))
                    Notify($"Fabricado: {ItemDb.NameOf(recipe.Result)} x{recipe.ResultCount}");
                else
                    Notify("Faltam materiais.");

                return;
            }
        }

        if (ShowInventory)
        {
            for (int slot = Inventory.HotbarSize; slot < Inventory.TotalSlots; slot++)
            {
                if (!BackpackSlotBounds(slot).Contains(mouse))
                    continue;

                // Clique na mochila manda o item para o espaço em uso da barra rápida.
                backpack.Swap(slot, backpack.Selected);
                return;
            }
        }

        for (int slot = 0; slot < Inventory.HotbarSize; slot++)
        {
            if (HotbarSlotBounds(slot).Contains(mouse))
            {
                backpack.Selected = slot;
                return;
            }
        }
    }

    public void Draw(SpriteBatch batch, Inventory backpack, Vector2 screen)
    {
        DrawVitals(batch);
        DrawClock(batch, screen);
        DrawHotbar(batch, backpack, screen);

        if (ShowInventory)
            DrawBackpack(batch, backpack);
        if (ShowCrafting)
            DrawCrafting(batch, backpack);
        if (ShowMinimap)
            DrawMinimap(batch, screen);

        DrawHeldItemName(batch, backpack, screen);

        if (_messageTimer > 0f)
        {
            float alpha = MathF.Min(1f, _messageTimer / 0.6f);
            var size = _font.MeasureText(_message);
            var position = new Vector2((screen.X - size.X) * 0.5f, screen.Y - 150f);
            batch.DrawRect(position - new Vector2(12f, 7f), size + new Vector2(24f, 14f),
                new Color(0f, 0f, 0f, 0.55f * alpha));
            _font.Draw(batch, _message, position, Color.White.WithAlpha(alpha));
        }

        if (ShowHelp)
            DrawHelp(batch, screen);

        if (_game.PlayerIsDead)
            DrawDeathOverlay(batch, screen);
    }

    // ---------------------------------------------------------------------
    //  Blocos da interface
    // ---------------------------------------------------------------------

    private void DrawVitals(SpriteBatch batch)
    {
        const float x = 20f, y = 20f, width = 236f, height = 22f;
        float ratio = Math.Clamp(_game.PlayerHealthRatio, 0f, 1f);

        batch.DrawRect(new Vector2(x - 3f, y - 3f), new Vector2(width + 6f, height + 6f), Panel);
        batch.DrawRect(new Vector2(x, y), new Vector2(width, height), new Color(0.22f, 0.08f, 0.10f, 1f));
        batch.DrawRect(new Vector2(x, y), new Vector2(width * ratio, height),
            new Color(0.85f, 0.24f, 0.28f, 1f));
        batch.DrawRect(new Vector2(x, y), new Vector2(width * ratio, height * 0.4f),
            new Color(1f, 0.45f, 0.45f, 0.55f));

        string label = $"{(int)MathF.Ceiling(_game.PlayerHealth)}/{(int)_game.PlayerMaxHealth}";
        _small.Draw(batch, label, new Vector2(x + 8f, y + 3f), Color.White);
    }

    private void DrawClock(SpriteBatch batch, Vector2 screen)
    {
        float top = ShowMinimap ? 20f + MinimapHeight + 10f : 20f;
        string[] lines =
        [
            $"Dia {_game.Day}   {_game.ClockText}",
            _game.BiomeName,
            _game.DepthText,
        ];

        float width = 0f;
        foreach (string line in lines)
            width = MathF.Max(width, _small.MeasureText(line).X);

        var origin = new Vector2(screen.X - width - 30f, top);
        batch.DrawRect(origin - new Vector2(10f, 8f), new Vector2(width + 20f, lines.Length * 22f + 14f), Panel);

        for (int i = 0; i < lines.Length; i++)
            _small.Draw(batch, lines[i], origin + new Vector2(0f, i * 22f), Color.FromBytes(226, 230, 240));
    }

    private void DrawHotbar(SpriteBatch batch, Inventory backpack, Vector2 screen)
    {
        var bounds = HotbarBounds();
        batch.DrawRect(bounds.Position - new Vector2(8f, 8f),
            bounds.Size + new Vector2(16f, 16f), Panel);

        for (int i = 0; i < Inventory.HotbarSize; i++)
            DrawSlot(batch, HotbarSlotBounds(i), backpack.Slots[i], i == backpack.Selected, $"{(i + 1) % 10}");

    }

    /// <summary>Nome do item na mão. Desenhado depois dos painéis: a mochila aberta termina
    /// bem em cima da barra rápida e engoliria o rótulo.</summary>
    private void DrawHeldItemName(SpriteBatch batch, Inventory backpack, Vector2 screen)
    {
        if (ItemDb.Get(backpack.SelectedItem) is not { } def)
            return;

        var size = _small.MeasureText(def.Name);
        var position = new Vector2((screen.X - size.X) * 0.5f, HotbarBounds().Y - 30f);
        batch.DrawRect(position - new Vector2(8f, 4f), size + new Vector2(16f, 8f),
            new Color(0f, 0f, 0f, 0.6f));
        _small.Draw(batch, def.Name, position, Selected);
    }

    private void DrawBackpack(SpriteBatch batch, Inventory backpack)
    {
        var bounds = InventoryBounds();
        batch.DrawRect(bounds.Position - new Vector2(8f, 8f), bounds.Size + new Vector2(16f, 16f), Panel);
        Outline(batch, bounds.Position - new Vector2(8f, 8f), bounds.Size + new Vector2(16f, 16f), PanelLine);

        for (int slot = Inventory.HotbarSize; slot < Inventory.TotalSlots; slot++)
            DrawSlot(batch, BackpackSlotBounds(slot), backpack.Slots[slot], false, null);

        _small.Draw(batch, "Clique em um item para levá-lo à mão",
            bounds.Position + new Vector2(0f, -30f), Color.FromBytes(190, 198, 214));
    }

    private void DrawCrafting(SpriteBatch batch, Inventory backpack)
    {
        var bounds = CraftingBounds();
        batch.DrawRect(bounds.Position, bounds.Size, Panel);
        Outline(batch, bounds.Position, bounds.Size, PanelLine);
        _font.Draw(batch, "Fabricação", bounds.Position + new Vector2(14f, 10f), Color.White);

        for (int i = 0; i < Recipes.All.Length; i++)
        {
            var recipe = Recipes.All[i];
            var row = RecipeBounds(i);
            bool can = Recipes.CanCraft(backpack, recipe);

            batch.DrawRect(row.Position, row.Size,
                can ? new Color(0.18f, 0.28f, 0.20f, 0.9f) : new Color(0.16f, 0.16f, 0.19f, 0.85f));

            ItemDb.DrawIcon(batch, _tileset, recipe.Result, row.Position + new Vector2(6f, 5f), 30f,
                can ? 1f : 0.5f);

            var textColor = can ? Color.White : Color.FromBytes(150, 150, 160);
            _small.Draw(batch, $"{ItemDb.NameOf(recipe.Result)} x{recipe.ResultCount}",
                row.Position + new Vector2(44f, 2f), textColor);

            string needs = string.Join("  ", recipe.Ingredients.Select(ingredient =>
                $"{ItemDb.NameOf(ingredient.Item)} {backpack.CountOf(ingredient.Item)}/{ingredient.Count}"));
            _small.Draw(batch, needs, row.Position + new Vector2(44f, 20f), textColor.WithAlpha(0.8f), 0.78f);
        }
    }

    private void DrawSlot(SpriteBatch batch, UiRect bounds, Inventory.Slot slot, bool selected, string? hotkey)
    {
        batch.DrawRect(bounds.Position, bounds.Size, SlotBack);
        Outline(batch, bounds.Position, bounds.Size, selected ? Selected : SlotLine, selected ? 2f : 1f);

        if (!slot.IsEmpty)
        {
            ItemDb.DrawIcon(batch, _tileset, slot.Item, bounds.Position + new Vector2(7f, 7f), bounds.Width - 14f);

            if (slot.Count > 1)
            {
                string count = slot.Count.ToString();
                var size = _small.MeasureText(count, 0.8f);
                _small.Draw(batch, count,
                    bounds.Position + new Vector2(bounds.Width - size.X - 4f, bounds.Height - 17f),
                    Color.White, 0.8f);
            }
        }

        if (hotkey is not null)
            _small.Draw(batch, hotkey, bounds.Position + new Vector2(4f, 1f),
                Color.FromBytes(170, 176, 190), 0.7f);
    }

    private void DrawHelp(SpriteBatch batch, Vector2 screen)
    {
        string[] lines =
        [
            "A/D mover · Espaço pular · botão esquerdo cavar/atacar · botão direito colocar",
            "1-0 escolher item · Tab mochila · C fabricação · M minimapa · F5 salvar · F9 carregar · H esconder ajuda",
        ];

        for (int i = 0; i < lines.Length; i++)
        {
            // Canto superior esquerdo, logo abaixo da barra de vida: embaixo a ajuda cobriria
            // a barra rápida.
            var position = new Vector2(20f, 56f + i * 22f);
            batch.DrawRect(position - new Vector2(8f, 4f),
                _small.MeasureText(lines[i], 0.85f) + new Vector2(16f, 8f), new Color(0f, 0f, 0f, 0.45f));
            _small.Draw(batch, lines[i], position, Color.FromBytes(214, 220, 232), 0.85f);
        }
    }

    private void DrawDeathOverlay(SpriteBatch batch, Vector2 screen)
    {
        batch.DrawRect(Vector2.Zero, screen, new Color(0.25f, 0.02f, 0.04f, 0.55f));

        string title = "Você caiu";
        string hint = "Renascendo no ponto inicial...";
        var titleSize = _font.MeasureText(title, 2f);
        var hintSize = _small.MeasureText(hint);

        _font.Draw(batch, title, new Vector2((screen.X - titleSize.X) * 0.5f, screen.Y * 0.4f),
            Color.FromBytes(255, 226, 226), 2f);
        _small.Draw(batch, hint, new Vector2((screen.X - hintSize.X) * 0.5f, screen.Y * 0.4f + 60f),
            Color.FromBytes(240, 200, 200));
    }

    // ---------------------------------------------------------------------
    //  Minimapa
    // ---------------------------------------------------------------------

    private void DrawMinimap(SpriteBatch batch, Vector2 screen)
    {
        var origin = new Vector2(screen.X - MinimapWidth - 20f, 20f);

        if (_minimapTimer <= 0f)
        {
            _minimapTimer = 0.4f;                          // 2,5 atualizações por segundo bastam
            RebuildMinimap();
        }

        batch.DrawRect(origin - new Vector2(4f, 4f), new Vector2(MinimapWidth + 8f, MinimapHeight + 8f), Panel);

        if (_minimap is not null)
            batch.Draw(_minimap, origin, new Vector2(MinimapWidth, MinimapHeight), Vector2.Zero, 0f, Color.White);

        Outline(batch, origin, new Vector2(MinimapWidth, MinimapHeight), PanelLine);

        // O jogador fica sempre no centro do recorte.
        batch.DrawRect(origin + new Vector2(MinimapWidth * 0.5f - 2f, MinimapHeight * 0.5f - 2f),
            new Vector2(4f, 4f), Color.White);
    }

    private void RebuildMinimap()
    {
        var tiles = _game.Tiles;
        if (tiles is null || _game.PlayerTransform is not { } player)
            return;

        int centerX = TileWorld.ToTile(player.Position.X);
        int centerY = TileWorld.ToTile(player.Position.Y);
        var pixels = new byte[MinimapTilesX * MinimapTilesY * 4];

        for (int y = 0; y < MinimapTilesY; y++)
        {
            int wy = centerY - MinimapTilesY / 2 + y;

            for (int x = 0; x < MinimapTilesX; x++)
            {
                int wx = centerX - MinimapTilesX / 2 + x;
                int i = (y * MinimapTilesX + x) * 4;

                Rgb color;
                byte alpha = 255;

                if (!tiles.InBounds(wx, wy))
                {
                    color = new Rgb(10, 10, 14);
                }
                else if (TileDb.Get(tiles.Get(wx, wy)) is { } def)
                {
                    color = def.Base;
                }
                else if (TileDb.Get(tiles.GetWall(wx, wy)) is { } wall)
                {
                    color = wall.Base.Scale(0.5f);
                }
                else
                {
                    // Céu aberto ou vazio subterrâneo.
                    color = wy <= tiles.SkyTop(Math.Clamp(wx, 0, tiles.Width - 1))
                        ? new Rgb(96, 148, 210)
                        : new Rgb(18, 18, 24);
                }

                pixels[i + 0] = color.R;
                pixels[i + 1] = color.G;
                pixels[i + 2] = color.B;
                pixels[i + 3] = alpha;
            }
        }

        _minimap?.Dispose();
        _minimap = Texture2D.FromPixels(_gl, MinimapTilesX, MinimapTilesY, pixels);
    }

    public void Dispose() => _minimap?.Dispose();

    // ---------------------------------------------------------------------
    //  Geometria dos painéis
    // ---------------------------------------------------------------------

    private static float HotbarWidth => Inventory.HotbarSize * SlotSize + (Inventory.HotbarSize - 1) * SlotGap;

    private UiRect HotbarBounds()
    {
        var screen = _game.DesignSize;
        return new UiRect((screen.X - HotbarWidth) * 0.5f, screen.Y - SlotSize - 18f, HotbarWidth, SlotSize);
    }

    private UiRect HotbarSlotBounds(int index)
    {
        var bounds = HotbarBounds();
        return new UiRect(bounds.X + index * (SlotSize + SlotGap), bounds.Y, SlotSize, SlotSize);
    }

    private UiRect InventoryBounds()
    {
        const int rows = (Inventory.TotalSlots - Inventory.HotbarSize) / Inventory.HotbarSize;
        var hotbar = HotbarBounds();
        float height = rows * SlotSize + (rows - 1) * SlotGap;
        return new UiRect(hotbar.X, hotbar.Y - height - 22f, HotbarWidth, height);
    }

    private UiRect BackpackSlotBounds(int slot)
    {
        int index = slot - Inventory.HotbarSize;
        int column = index % Inventory.HotbarSize;
        int row = index / Inventory.HotbarSize;
        var bounds = InventoryBounds();
        return new UiRect(bounds.X + column * (SlotSize + SlotGap), bounds.Y + row * (SlotSize + SlotGap),
            SlotSize, SlotSize);
    }

    // Começa abaixo das duas linhas de ajuda (que ficam no topo à esquerda) e termina bem
    // acima da barra rápida.
    private static UiRect CraftingBounds()
        => new(20f, 110f, 340f, 46f + Recipes.All.Length * 44f);

    private static UiRect RecipeBounds(int index)
    {
        var panel = CraftingBounds();
        return new UiRect(panel.X + 10f, panel.Y + 42f + index * 44f, panel.Width - 20f, 40f);
    }

    private static void Outline(SpriteBatch batch, Vector2 position, Vector2 size, Color color, float thickness = 1f)
    {
        batch.DrawRect(position, new Vector2(size.X, thickness), color);
        batch.DrawRect(position + new Vector2(0f, size.Y - thickness), new Vector2(size.X, thickness), color);
        batch.DrawRect(position, new Vector2(thickness, size.Y), color);
        batch.DrawRect(position + new Vector2(size.X - thickness, 0f), new Vector2(thickness, size.Y), color);
    }
}
