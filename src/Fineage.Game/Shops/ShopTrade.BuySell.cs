namespace Fineage.Game.Shops;

public static partial class ShopTrade
{
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
}
