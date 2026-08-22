namespace Aurora.SlandSurvivor.Items;

public sealed record Ingredient(int Item, int Count);

public sealed record Recipe(int Result, int ResultCount, params Ingredient[] Ingredients);

/// <summary>
/// Receitas da fabricação (tecla C). Sem bancada nem fornalha: a progressão vem dos
/// ingredientes (ferro exige picareta de pedra, cristal exige picareta de ferro), não de
/// móveis que o jogador teria que carregar.
/// </summary>
public static class Recipes
{
    public static readonly Recipe[] All =
    [
        new(ItemIds.Torch, 4, new Ingredient(ItemIds.Wood, 1), new Ingredient(ItemIds.Coal, 1)),
        new(ItemIds.Planks, 4, new Ingredient(ItemIds.Wood, 1)),
        new(ItemIds.Brick, 2, new Ingredient(ItemIds.Stone, 4)),
        new(ItemIds.Glass, 2, new Ingredient(ItemIds.Sand, 3), new Ingredient(ItemIds.Coal, 1)),
        new(ItemIds.IronBar, 1, new Ingredient(ItemIds.IronOre, 3), new Ingredient(ItemIds.Coal, 1)),
        new(ItemIds.GoldBar, 1, new Ingredient(ItemIds.GoldOre, 3), new Ingredient(ItemIds.Coal, 1)),
        new(ItemIds.StonePick, 1, new Ingredient(ItemIds.Stone, 12), new Ingredient(ItemIds.Wood, 3)),
        new(ItemIds.IronPick, 1, new Ingredient(ItemIds.IronBar, 4), new Ingredient(ItemIds.Wood, 3)),
        new(ItemIds.IronSword, 1, new Ingredient(ItemIds.IronBar, 3), new Ingredient(ItemIds.Wood, 2)),
        new(ItemIds.Bandage, 1, new Ingredient(ItemIds.Gel, 4)),
    ];

    public static bool CanCraft(Inventory inventory, Recipe recipe)
    {
        foreach (var ingredient in recipe.Ingredients)
        {
            if (!inventory.Has(ingredient.Item, ingredient.Count))
                return false;
        }

        return true;
    }

    /// <summary>Fabrica se der; devolve false (sem gastar nada) se faltar ingrediente.</summary>
    public static bool Craft(Inventory inventory, Recipe recipe)
    {
        if (!CanCraft(inventory, recipe))
            return false;

        foreach (var ingredient in recipe.Ingredients)
            inventory.Consume(ingredient.Item, ingredient.Count);

        inventory.Add(recipe.Result, recipe.ResultCount);
        return true;
    }
}
