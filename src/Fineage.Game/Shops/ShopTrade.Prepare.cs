namespace Fineage.Game.Shops;

public static partial class ShopTrade
{
    public static PreparedShopEntry PrepareEntry(
        MultiSellEntry entry,
        ItemInstance? item,
        bool maintain,
        bool applyCastleTax,
        double taxRate,
        ItemCatalog catalog)
    {
        int entryId = entry.Id * 100_000;
        if (maintain && item != null)
            entryId += item.EnchantLevel;

        int tax = 0;
        int adena = 0;
        var ingredients = new List<PreparedShopPart>();
        foreach (var ingredient in entry.Ingredients)
        {
            if (ingredient.ItemId == AdenaItemId)
            {
                if (ingredient.Tax)
                {
                    if (applyCastleTax)
                        tax += (int)Math.Round(ingredient.Count * taxRate, MidpointRounding.AwayFromZero);
                }
                else
                {
                    adena += ingredient.Count;
                }

                continue;
            }

            int enchant = -1;
            if (maintain && item != null && IsGear(catalog, ingredient.ItemId))
                enchant = item.EnchantLevel;
            else if (ingredient.Enchant > 0)
                enchant = ingredient.Enchant;
            ingredients.Add(new PreparedShopPart(ingredient.ItemId, ingredient.Count, enchant));
        }

        adena += tax;
        if (adena > 0)
            ingredients.Add(new PreparedShopPart(AdenaItemId, adena, 0));

        bool stackable = true;
        var products = new List<PreparedShopPart>();
        foreach (var product in entry.Products)
        {
            if (!catalog.TryGet(product.ItemId, out var template) || !template.Stackable)
                stackable = false;
            int enchant = product.Enchant;
            if (maintain && item != null && IsGear(catalog, product.ItemId))
                enchant = item.EnchantLevel;
            products.Add(new PreparedShopPart(product.ItemId, product.Count, enchant));
        }

        return new PreparedShopEntry(entryId, entry.Id, stackable, tax, ingredients, products);
    }

    static bool IsGear(ItemCatalog catalog, int itemId) =>
        catalog.TryGet(itemId, out var template) && template.Kind is ItemKind.Weapon or ItemKind.Armor;

    static int CountUnequipped(PlayerInventory inventory, int itemId, int enchant)
    {
        int total = 0;
        foreach (var item in inventory.Items)
        {
            if (item.ItemId != itemId || item.IsEquipped)
                continue;
            if (enchant >= 0 && item.EnchantLevel != enchant)
                continue;
            total += item.Template.Stackable ? item.Count : 1;
        }

        return total;
    }

    static List<ItemInstance> Unique(PlayerInventory inventory, bool byEnchant)
    {
        var result = new List<ItemInstance>();
        foreach (var item in inventory.Items)
        {
            if (item.ItemId is AdenaItemId or AncientAdenaItemId)
                continue;
            bool duplicate = false;
            foreach (var added in result)
            {
                if (added.ItemId == item.ItemId && (!byEnchant || added.EnchantLevel == item.EnchantLevel))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                result.Add(item);
        }

        return result;
    }

    static void PayAdena(PlayerInventory inventory, int amount, List<ShopTouch> touches)
    {
        if (amount < 1)
            return;
        if (!inventory.TryConsume(AdenaItemId, amount, out var stack, out bool removed) || stack == null)
            throw new InvalidOperationException("Adena check passed but the bag could not pay.");
        touches.Add(new ShopTouch(removed ? ShopTouchKind.Remove : ShopTouchKind.Modify, stack));
    }

    static void GainAdena(PlayerInventory inventory, int amount, List<ShopTouch> touches)
    {
        if (amount < 1)
            return;
        bool existed = inventory.FindByItemId(AdenaItemId) != null;
        var stack = inventory.Add(AdenaItemId, amount);
        touches.Add(new ShopTouch(existed ? ShopTouchKind.Modify : ShopTouchKind.Add, stack));
    }

    static void Remember(List<ShopTouch> touches, ItemInstance item, bool modify)
    {
        foreach (var touch in touches)
        {
            if (touch.Item.ObjectId == item.ObjectId)
                return;
        }

        touches.Add(new ShopTouch(modify ? ShopTouchKind.Modify : ShopTouchKind.Add, item));
    }

    sealed record PlannedBuy(BuyProduct Product, ItemTemplate Template, int Count, int Unit);
}
