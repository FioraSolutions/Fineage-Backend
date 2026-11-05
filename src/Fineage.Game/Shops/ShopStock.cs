namespace Fineage.Game.Shops;

/// <summary>
/// Limited buylist counts. A purchase arms the restock timer from that moment,
/// the same way Mobius <c>BuyListTaskManager</c> does. Unlimited rows are not stored.
/// </summary>
public sealed class ShopStock
{
    readonly Dictionary<(int ListId, int ItemId), Line> _lines = [];

    public ShopStock(BuyListCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var list in catalog.Lists.Values)
        {
            foreach (var product in list.Products)
            {
                if (product.Limited)
                    _lines[(list.Id, product.ItemId)] = new Line(product.MaxCount, product.RestockMinutes);
            }
        }
    }

    public int Available(int listId, int itemId, long nowUnixMs)
    {
        if (!_lines.TryGetValue((listId, itemId), out var line))
            return -1;
        RestockIfDue(line, nowUnixMs);
        return line.Current;
    }

    public bool TryTake(int listId, int itemId, int count, long nowUnixMs)
    {
        if (count < 1)
            return false;
        if (!_lines.TryGetValue((listId, itemId), out var line))
            return true;
        RestockIfDue(line, nowUnixMs);
        if (line.Current < count)
            return false;
        line.Current -= count;
        long delay = line.RestockMinutes > 0 ? line.RestockMinutes * 60_000L : 0;
        line.RestockAt = nowUnixMs + delay;
        return true;
    }

    static void RestockIfDue(Line line, long nowUnixMs)
    {
        if (line.RestockAt > 0 && nowUnixMs >= line.RestockAt)
        {
            line.Current = line.Max;
            line.RestockAt = 0;
        }
    }

    sealed class Line
    {
        public Line(int max, int restockMinutes)
        {
            Max = max;
            Current = max;
            RestockMinutes = restockMinutes;
        }

        public int Max { get; }
        public int Current { get; set; }
        public int RestockMinutes { get; }
        public long RestockAt { get; set; }
    }
}
