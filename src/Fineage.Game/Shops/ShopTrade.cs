namespace Fineage.Game.Shops;

/// <summary>
/// Buy, sell, and multisell checks. Formulas follow L2J Mobius CT 0 Interlude
/// <c>RequestBuyItem</c>, <c>RequestSellItem</c>, <c>MultiSellChoose</c>,
/// <c>PreparedListContainer</c>, and <c>MerchantPriceConfig</c>.
/// Sell price is the item reference price divided by 2.
/// Buy price is <c>base * (1 + townBaseTax/100 + castleTax/100)</c>, truncated toward zero.
/// Castle tax is supplied by the caller and is 0 when the castle has no owner.
/// </summary>
public static class ShopTrade
{
    public const int AdenaItemId = 57;
    public const int AncientAdenaItemId = 5575;
    public const int MaxAdena = 2_000_000_000;
    public const int MaxCountPerLine = 10_000;
    public const int MaxLines = 250;
    public const int SlotsNoDwarf = 80;
    public const int SlotsDwarf = 100;
    public const int DwarfRace = 4;
    public const int WeightLimitBase = 69_000;
    public const int SiegeGuardFirst = 3960;
    public const int SiegeGuardLast = 4026;

    /// <summary>Mobius <c>RateSiegeGuardsPrice</c> default.</summary>
    public const double SiegeGuardPriceRate = 1;

    public static int SlotLimit(int race) => race == DwarfRace ? SlotsDwarf : SlotsNoDwarf;

    /// <summary>Mobius <c>Creature.getMaxLoad</c> without a weight-limit skill: floor(CON bonus * 69000).</summary>
    public static int MaxLoad(ClassCatalog classes, int classId)
    {
        ArgumentNullException.ThrowIfNull(classes);
        if (!classes.TryGet(classId, out var template) || template == null)
            return WeightLimitBase;
        double bonus = classes.StatBonuses.Bonus(StatSlot.Con, template.Con);
        return (int)Math.Floor(bonus * WeightLimitBase);
    }

    public static int TaxedUnitPrice(int basePrice, int baseTaxPercent, int castleTaxPercent)
    {
        if (basePrice < 0)
            return -1;
        if (basePrice == 0)
            return 0;
        double taxed = basePrice * (1d + (baseTaxPercent + castleTaxPercent) / 100d);
        if (taxed < 0 || taxed > MaxAdena)
            return -1;
        return (int)taxed;
    }

    public static ShopMutation Buy(
        PlayerInventory inventory,
        ItemCatalog catalog,
        BuyListDefinition list,
        IReadOnlyList<ShopLine> lines,
        int baseTaxPercent,
        int castleTaxPercent,
        int maxLoad,
        int slotLimit,
        ShopStock stock,
        long nowUnixMs)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(stock);

        if (lines.Count < 1 || lines.Count > MaxLines)
            return ShopMutation.Fail(ShopDeny.Quantity);

        var plan = new List<PlannedBuy>();
        var wanted = new Dictionary<int, int>();
        long subtotal = 0;
        long addedWeight = 0;
        long slots = 0;
        foreach (var line in lines)
        {
            if (line.ItemId < 1 || line.Count < 1 || line.Count > MaxCountPerLine)
                return ShopMutation.Fail(ShopDeny.Quantity);
            var product = list.Find(line.ItemId);
            if (product == null || !catalog.TryGet(line.ItemId, out var template))
                return ShopMutation.Fail(ShopDeny.NotTradable);
            if (!template.Stackable && line.Count > 1)
                return ShopMutation.Fail(ShopDeny.Quantity);

            int price = product.Price;
            if (line.ItemId >= SiegeGuardFirst && line.ItemId <= SiegeGuardLast)
            {
                double scaled = price * SiegeGuardPriceRate;
                if (scaled < 0 || scaled > MaxAdena)
                    return ShopMutation.Fail(ShopDeny.Pocket);
                price = (int)scaled;
            }

            int unit = TaxedUnitPrice(price, baseTaxPercent, castleTaxPercent);
            if (unit < 0)
                return ShopMutation.Fail(ShopDeny.Pocket);
            if (unit == 0)
                return ShopMutation.Fail(ShopDeny.NotTradable);
            if (MaxAdena / line.Count < unit)
                return ShopMutation.Fail(ShopDeny.Pocket);
            subtotal += (long)line.Count * unit;
            if (subtotal > MaxAdena)
                return ShopMutation.Fail(ShopDeny.Pocket);

            wanted[line.ItemId] = wanted.GetValueOrDefault(line.ItemId) + line.Count;
            addedWeight += (long)template.Weight * line.Count;
            if (inventory.FindByItemId(line.ItemId) == null)
                slots++;
            plan.Add(new PlannedBuy(product, template, line.Count, unit));
        }

        foreach (var (itemId, count) in wanted)
        {
            var product = list.Find(itemId);
            if (product is { Limited: true } && count > stock.Available(list.Id, itemId, nowUnixMs))
                return ShopMutation.Fail(ShopDeny.Stock);
        }

        if (inventory.TotalWeight + addedWeight > maxLoad)
            return ShopMutation.Fail(ShopDeny.Weight);
        if (inventory.Items.Count + slots > slotLimit)
            return ShopMutation.Fail(ShopDeny.Slots);
        if (subtotal > inventory.CountOf(AdenaItemId))
            return ShopMutation.Fail(ShopDeny.Adena);

        foreach (var (itemId, count) in wanted)
        {
            if (list.Find(itemId) is { Limited: true } && !stock.TryTake(list.Id, itemId, count, nowUnixMs))
                return ShopMutation.Fail(ShopDeny.Stock);
        }

        var result = new ShopMutation();
        PayAdena(inventory, (int)subtotal, result.Touches);
        foreach (var step in plan)
        {
            bool existed = inventory.FindByItemId(step.Product.ItemId) != null;
            foreach (var piece in inventory.AddPieces(step.Product.ItemId, step.Count))
                Remember(result.Touches, piece, existed && piece.Template.Stackable);
        }

        return result;
    }

    public static ShopMutation Sell(PlayerInventory inventory, IReadOnlyList<ShopObjectLine> lines)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count < 1 || lines.Count > MaxLines)
            return ShopMutation.Fail(ShopDeny.Quantity);

        var plan = new List<(ItemInstance Item, int Count, int Unit)>();
        long total = 0;
        var seen = new HashSet<int>();
        foreach (var line in lines)
        {
            if (line.ObjectId < 1 || line.Count < 1 || line.Count > MaxCountPerLine || !seen.Add(line.ObjectId))
                return ShopMutation.Fail(ShopDeny.Quantity);
            var item = inventory.Find(line.ObjectId);
            if (item == null || item.IsEquipped || item.Count < line.Count || !item.Template.CanSell)
                continue;
            int unit = item.Template.ReferencePrice / 2;
            if (unit < 1)
                continue;
            if (MaxAdena / line.Count < unit)
                return ShopMutation.Fail(ShopDeny.Pocket);
            total += (long)unit * line.Count;
            if (total > MaxAdena)
                return ShopMutation.Fail(ShopDeny.Pocket);
            plan.Add((item, line.Count, unit));
        }

        if (plan.Count == 0)
            return ShopMutation.Fail(ShopDeny.NotTradable);

        long adena = inventory.CountOf(AdenaItemId);
        if (adena > MaxAdena || adena + total > MaxAdena)
            return ShopMutation.Fail(ShopDeny.Pocket);

        var result = new ShopMutation();
        foreach (var (item, count, _) in plan)
        {
            if (!inventory.TryReduce(item, count, out bool removed))
                return ShopMutation.Fail(ShopDeny.NotTradable);
            result.Touches.Add(new ShopTouch(removed ? ShopTouchKind.Remove : ShopTouchKind.Modify, item));
        }

        GainAdena(inventory, (int)total, result.Touches);
        return result;
    }

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

public enum ShopDeny
{
    Quantity,
    NotTradable,
    Pocket,
    Stock,
    Weight,
    Slots,
    Adena,
    Ingredients
}

public enum ShopTouchKind
{
    Add,
    Modify,
    Remove
}

public readonly record struct ShopLine(int ItemId, int Count);

public readonly record struct ShopObjectLine(int ObjectId, int Count);

public readonly record struct ShopTouch(ShopTouchKind Kind, ItemInstance Item);

public sealed class ShopMutation
{
    public ShopDeny? Deny { get; private init; }
    public int MissingCount { get; private init; }
    public string MissingName { get; private init; } = "";
    public List<ShopTouch> Touches { get; } = [];
    public List<ItemInstance> Taken { get; } = [];

    public bool Ok => Deny == null;

    public static ShopMutation Fail(ShopDeny deny, int missingCount = 0, string? missingName = null) => new()
    {
        Deny = deny,
        MissingCount = missingCount,
        MissingName = missingName ?? ""
    };
}

public sealed class PreparedMultiSell
{
    readonly Dictionary<int, PreparedShopEntry> _byId;

    public PreparedMultiSell(int listId, bool maintainEnchantment, bool inventoryOnly, IReadOnlyList<PreparedShopEntry> entries)
    {
        ListId = listId;
        MaintainEnchantment = maintainEnchantment;
        InventoryOnly = inventoryOnly;
        Entries = entries;
        _byId = new Dictionary<int, PreparedShopEntry>();
        foreach (var entry in entries)
            _byId[entry.EntryId] = entry;
    }

    public int ListId { get; }
    public bool MaintainEnchantment { get; }
    public bool InventoryOnly { get; }
    public IReadOnlyList<PreparedShopEntry> Entries { get; }

    public PreparedShopEntry? Find(int entryId) => _byId.GetValueOrDefault(entryId);
}

public sealed record PreparedShopEntry(
    int EntryId,
    int TemplateEntryId,
    bool Stackable,
    int TaxAmount,
    IReadOnlyList<PreparedShopPart> Ingredients,
    IReadOnlyList<PreparedShopPart> Products);

public sealed record PreparedShopPart(int ItemId, int Count, int Enchant);
