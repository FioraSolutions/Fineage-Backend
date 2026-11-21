namespace Fineage.Game.Economy;

/// <summary>Builds starter-kit and inventory grant postings against a system account.</summary>
public static class LedgerInventoryChange
{
    public static LedgerHoldings HoldingsFrom(PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var holdings = new LedgerHoldings();
        foreach (var item in inventory.Items)
        {
            if (item.Template.Stackable)
                holdings.Add(item.ItemId, item.Count);
            else if (item.Count == 1)
                holdings.AddPiece(item.ItemId, item.ObjectId);
            else
                throw new InvalidOperationException($"non-stackable item {item.ObjectId} has count {item.Count}");
        }

        return holdings;
    }

    /// <summary>
    /// Posts everything <paramref name="account"/> gained and lost between two observations, with the system account
    /// <paramref name="counterpart"/> on the other side of every change: pieces by instance id, the rest by count.
    /// </summary>
    public static void PostChange(
        LedgerMove move, LedgerAccount account, LedgerHoldings before, LedgerHoldings after, LedgerAccount counterpart)
    {
        if (!counterpart.IsSystem)
            throw new ArgumentException("the counterpart of a change takes no pieces, so it must be a system account", nameof(counterpart));
        var pieces = new Dictionary<int, long>();
        void Pieces(LedgerHoldings from, LedgerHoldings without, int sign)
        {
            foreach (var group in from.Instances
                         .Where(piece => !without.Instances.TryGetValue(piece.Key, out int item) || item != piece.Value)
                         .GroupBy(piece => piece.Value, piece => piece.Key))
            {
                var ids = group.Order().ToList();
                move.Post(account, group.Key, sign * ids.Count, ids);
                move.Post(counterpart, group.Key, -sign * ids.Count);
                pieces[group.Key] = pieces.GetValueOrDefault(group.Key) + sign * ids.Count;
            }
        }

        Pieces(before, after, -1);
        Pieces(after, before, 1);
        foreach (int item in before.Counts.Keys.Union(after.Counts.Keys).Order())
        {
            long rest = after.CountOf(item) - before.CountOf(item) - pieces.GetValueOrDefault(item);
            move.Post(account, item, rest);
            move.Post(counterpart, item, -rest);
        }
    }
}