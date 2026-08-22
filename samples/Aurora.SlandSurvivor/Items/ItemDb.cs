using System.Numerics;
using Aurora.Runtime.Graphics;
using Aurora.SlandSurvivor.Worlds;

namespace Aurora.SlandSurvivor.Items;

/// <summary>Ids de item. Separados dos ids de tile de propósito: barra de ferro e gel não
/// são blocos, e grama dropa terra.</summary>
public static class ItemIds
{
    public const int None = -1;

    public const int Dirt = 0, Stone = 1, Wood = 2, Sand = 3, Sandstone = 4, Snow = 5,
                     Ice = 6, Planks = 7, Brick = 8, Torch = 9, Coal = 10, IronOre = 11,
                     GoldOre = 12, Gem = 13, IronBar = 14, GoldBar = 15, Gel = 16,
                     Leaves = 17, Glass = 18, Clay = 19, Mud = 20, Deepstone = 21,
                     Cactus = 22, StonePick = 23, IronPick = 24, IronSword = 25, Bandage = 26;

    public const int Count = 27;
}

public enum ItemKind { Block, Material, Pickaxe, Sword, Consumable }

/// <param name="PlaceTile">Tile colocado ao usar com o botão direito, ou -1.</param>
/// <param name="Power">Poder de picareta (0 = não é picareta).</param>
/// <param name="Damage">Dano do golpe corpo a corpo com este item na mão.</param>
public sealed record ItemDef(
    int Id,
    string Name,
    ItemKind Kind,
    int MaxStack,
    int PlaceTile = ItemIds.None,
    int Power = 0,
    float Damage = 8f,
    float Heal = 0f,
    Rgb Color = default);

public static class ItemDb
{
    /// <summary>Dano do soco / de qualquer item que não seja arma.</summary>
    public const float FistDamage = 8f;

    /// <summary>Poder de mineração sem picareta melhor na mão.</summary>
    public const int BasePower = 1;

    private static readonly ItemDef?[] Defs = new ItemDef?[ItemIds.Count];

    static ItemDb()
    {
        Block(ItemIds.Dirt, "Terra", TileId.Dirt);
        Block(ItemIds.Stone, "Pedra", TileId.Stone);
        Block(ItemIds.Wood, "Madeira", TileId.Wood);
        Block(ItemIds.Sand, "Areia", TileId.Sand);
        Block(ItemIds.Sandstone, "Arenito", TileId.Sandstone);
        Block(ItemIds.Snow, "Neve", TileId.Snow);
        Block(ItemIds.Ice, "Gelo", TileId.Ice);
        Block(ItemIds.Planks, "Tábuas", TileId.Planks);
        Block(ItemIds.Brick, "Tijolo de pedra", TileId.Brick);
        Block(ItemIds.Torch, "Tocha", TileId.Torch);
        Block(ItemIds.Leaves, "Folhas", TileId.Leaves);
        Block(ItemIds.Glass, "Vidro", TileId.Glass);
        Block(ItemIds.Clay, "Argila", TileId.Clay);
        Block(ItemIds.Mud, "Lama", TileId.Mud);
        Block(ItemIds.Deepstone, "Rocha profunda", TileId.Deepstone);
        Block(ItemIds.Cactus, "Cacto", TileId.Cactus);

        // Minérios ainda são blocos colocáveis (dá para marcar caminho com eles).
        Block(ItemIds.Coal, "Carvão", TileId.Coal);
        Block(ItemIds.IronOre, "Minério de ferro", TileId.IronOre);
        Block(ItemIds.GoldOre, "Minério de ouro", TileId.GoldOre);
        Block(ItemIds.Gem, "Cristal", TileId.GemOre);

        Add(new ItemDef(ItemIds.IronBar, "Barra de ferro", ItemKind.Material, 99,
            Color: new Rgb(206, 168, 138)));
        Add(new ItemDef(ItemIds.GoldBar, "Barra de ouro", ItemKind.Material, 99,
            Color: new Rgb(238, 202, 84)));
        Add(new ItemDef(ItemIds.Gel, "Gosma", ItemKind.Material, 99,
            Color: new Rgb(110, 190, 236)));

        Add(new ItemDef(ItemIds.StonePick, "Picareta de pedra", ItemKind.Pickaxe, 1,
            Power: 2, Damage: 11f, Color: new Rgb(150, 150, 158)));
        Add(new ItemDef(ItemIds.IronPick, "Picareta de ferro", ItemKind.Pickaxe, 1,
            Power: 3, Damage: 14f, Color: new Rgb(206, 168, 138)));
        Add(new ItemDef(ItemIds.IronSword, "Espada de ferro", ItemKind.Sword, 1,
            Damage: 26f, Color: new Rgb(222, 222, 232)));
        Add(new ItemDef(ItemIds.Bandage, "Bandagem de gosma", ItemKind.Consumable, 20,
            Heal: 40f, Color: new Rgb(226, 240, 250)));
    }

    private static void Add(ItemDef def) => Defs[def.Id] = def;

    private static void Block(int id, string name, int tile)
        => Add(new ItemDef(id, name, ItemKind.Block, 99, tile,
            Color: TileDb.Get(tile)?.Base ?? new Rgb(200, 200, 200)));

    public static ItemDef? Get(int id) => id >= 0 && id < ItemIds.Count ? Defs[id] : null;

    public static string NameOf(int id) => Get(id)?.Name ?? "-";

    /// <summary>Poder de mineração com este item na mão (nunca abaixo do básico).</summary>
    public static int PowerOf(int id) => Math.Max(BasePower, Get(id)?.Power ?? 0);

    /// <summary>Dano do golpe com este item na mão.</summary>
    public static float DamageOf(int id) => Get(id)?.Damage ?? FistDamage;

    // ---------------------------------------------------------------------
    //  Ícone
    // ---------------------------------------------------------------------

    /// <summary>
    /// Desenha o ícone de um item. Bloco: recorta o próprio tile do atlas (o inventário fica
    /// coerente com o mundo de graça). Resto: formas simples montadas com retângulos, para
    /// não precisar de um segundo atlas só para 7 itens.
    /// </summary>
    public static void DrawIcon(SpriteBatch batch, Texture2D tileset, int itemId,
        Vector2 position, float size, float alpha = 1f)
    {
        if (Get(itemId) is not { } def)
            return;

        var tint = Color.White.WithAlpha(alpha);

        if (def.Kind == ItemKind.Block && def.PlaceTile >= 0)
        {
            int tile = def.PlaceTile;
            var source = new RectF(
                tile % TileId.PerRow * TileId.Size, tile / TileId.PerRow * TileId.Size,
                TileId.Size, TileId.Size);
            batch.Draw(tileset, position, new Vector2(size, size), Vector2.Zero, 0f, tint, source);
            return;
        }

        var main = ToColor(def.Color, alpha);
        var dark = ToColor(def.Color.Scale(0.6f), alpha);
        float u = size / 16f;                              // 1 "pixel" do ícone

        switch (def.Kind)
        {
            case ItemKind.Pickaxe:
                batch.DrawRect(position + new Vector2(7f * u, 4f * u), new Vector2(2f * u, 11f * u), dark);
                batch.DrawRect(position + new Vector2(2f * u, 3f * u), new Vector2(12f * u, 2f * u), main);
                batch.DrawRect(position + new Vector2(2f * u, 5f * u), new Vector2(3f * u, 2f * u), main);
                batch.DrawRect(position + new Vector2(11f * u, 5f * u), new Vector2(3f * u, 2f * u), main);
                break;

            case ItemKind.Sword:
                batch.DrawRect(position + new Vector2(7f * u, 1f * u), new Vector2(2f * u, 10f * u), main);
                batch.DrawRect(position + new Vector2(4f * u, 10f * u), new Vector2(8f * u, 2f * u), dark);
                batch.DrawRect(position + new Vector2(7f * u, 12f * u), new Vector2(2f * u, 3f * u), dark);
                break;

            case ItemKind.Consumable:
                batch.DrawRect(position + new Vector2(5f * u, 2f * u), new Vector2(6f * u, 2f * u), dark);
                batch.DrawRect(position + new Vector2(3f * u, 4f * u), new Vector2(10f * u, 10f * u), main);
                break;

            default:   // barras, gosma: lingote em perspectiva
                batch.DrawRect(position + new Vector2(3f * u, 5f * u), new Vector2(10f * u, 6f * u), main);
                batch.DrawRect(position + new Vector2(4f * u, 3f * u), new Vector2(8f * u, 2f * u),
                    ToColor(def.Color.Scale(1.25f), alpha));
                batch.DrawRect(position + new Vector2(3f * u, 11f * u), new Vector2(10f * u, 2f * u), dark);
                break;
        }
    }

    public static Color ToColor(Rgb rgb, float alpha = 1f)
        => new(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, alpha);
}
