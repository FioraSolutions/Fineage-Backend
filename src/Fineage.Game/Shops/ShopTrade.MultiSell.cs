namespace Fineage.Game.Shops;

public static partial class ShopTrade
{
    public static PreparedMultiSell Prepare(
        MultiSellListDefinition list,
        PlayerInventory inventory,
        ItemCatalog catalog,
        bool inventoryOnly,
        bool applyCastleTax,
        int castleTaxPercent)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(catalog);
        bool maintain = inventoryOnly && list.MaintainEnchantment;
        double taxRate = applyCastleTax ? castleTaxPercent / 100d : 0;
        var entries = new List<PreparedShopEntry>();
        if (inventoryOnly)
        {
            var unique = Unique(inventory, maintain);
            foreach (var entry in list.Entries)
            {
                if (entry.Ingredients.Count == 0)
                    continue;
                int ingredientId = entry.Ingredients[0].ItemId;
                ItemInstance? match = null;
                foreach (var item in unique)
                {
                    if (!item.IsEquipped && item.ItemId == ingredientId)
                    {
                        match = item;
                        break;
                    }
                }

                if (match != null)
                    entries.Add(PrepareEntry(entry, match, maintain, applyCastleTax, taxRate, catalog));
            }
        }
        else
        {
            foreach (var entry in list.Entries)
                entries.Add(PrepareEntry(entry, item: null, maintain: false, applyCastleTax, taxRate, catalog));
        }

        return new PreparedMultiSell(list.Id, maintain, inventoryOnly, entries);
    }

    public static ShopMutation Exchange(
        PlayerInventory inventory,
        ItemCatalog catalog,
        PreparedShopEntry entry,
        int amount,
        int maxLoad,
        int slotLimit)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(entry);
        if (amount < 1 || amount > MaxCountPerLine)
            return ShopMutation.Fail(ShopDeny.Quantity);
        if (!entry.Stackable && amount > 1)
            return ShopMutation.Fail(ShopDeny.Quantity);

        foreach (var part in entry.Ingredients.Concat(entry.Products))
        {
            if (part.ItemId < 0)
                return ShopMutation.Fail(ShopDeny.NotTradable);
            if (part.ItemId > 0 && !catalog.TryGet(part.ItemId, out _))
                return ShopMutation.Fail(ShopDeny.NotTradable);
        }

        long addedWeight = 0;
        long slots = 0;
        foreach (var product in entry.Products)
        {
            if (!catalog.TryGet(product.ItemId, out var template))
                return ShopMutation.Fail(ShopDeny.NotTradable);
            long count = (long)product.Count * amount;
            if (count < 1 || count > MaxAdena)
                return ShopMutation.Fail(ShopDeny.Quantity);
            addedWeight += (long)template.Weight * count;
            if (!template.Stackable)
                slots += count;
            else if (inventory.FindByItemId(product.ItemId) == null)
                slots++;
        }

        if (inventory.TotalWeight + addedWeight > maxLoad)
            return ShopMutation.Fail(ShopDeny.Weight);
        if (inventory.Items.Count + slots > slotLimit)
            return ShopMutation.Fail(ShopDeny.Slots);

        var required = new List<PreparedShopPart>();
        foreach (var ingredient in entry.Ingredients)
        {
            PreparedShopPart? existing = null;
            foreach (var row in required)
            {
                if (row.ItemId == ingredient.ItemId && row.Enchant == ingredient.Enchant)
                {
                    existing = row;
                    break;
                }
            }

            long count = (long)ingredient.Count * amount;
            if (existing != null)
                count += existing.Count;
            if (count < 1 || count > int.MaxValue)
                return ShopMutation.Fail(ShopDeny.Quantity);
            if (existing == null)
                required.Add(new PreparedShopPart(ingredient.ItemId, (int)count, ingredient.Enchant));
            else
                required[required.IndexOf(existing)] = existing with { Count = (int)count };
        }

        string? missingName = null;
        int missingCount = 0;
        foreach (var ingredient in required)
        {
            int have = CountUnequipped(inventory, ingredient.ItemId, ingredient.Enchant);
            if (have < ingredient.Count)
            {
                missingCount = ingredient.Count;
                missingName = catalog.TryGet(ingredient.ItemId, out var template) ? template.Name : ingredient.ItemId.ToString();
                break;
            }
        }

        if (missingName != null)
            return ShopMutation.Fail(ShopDeny.Ingredients, missingCount, missingName);

        var result = new ShopMutation();
        foreach (var ingredient in entry.Ingredients)
        {
            long count = (long)ingredient.Count * amount;
            if (count > int.MaxValue || !inventory.TryTakeUnequipped(ingredient.ItemId, (int)count, ingredient.Enchant, result.Taken))
                return ShopMutation.Fail(ShopDeny.Ingredients, ingredient.Count * amount, ingredient.ItemId.ToString());
        }

        foreach (var taken in result.Taken)
            result.Touches.Add(new ShopTouch(taken.Count == 0 ? ShopTouchKind.Remove : ShopTouchKind.Modify, taken));

        foreach (var product in entry.Products)
        {
            if (!catalog.TryGet(product.ItemId, out var template))
                return ShopMutation.Fail(ShopDeny.NotTradable);
            int count = checked(product.Count * amount);
            bool existed = inventory.FindByItemId(product.ItemId) != null;
            int enchant = template.Stackable ? 0 : product.Enchant;
            foreach (var piece in inventory.AddPieces(product.ItemId, count, enchant))
                Remember(result.Touches, piece, existed && template.Stackable);
        }

        return result;
    }
}
