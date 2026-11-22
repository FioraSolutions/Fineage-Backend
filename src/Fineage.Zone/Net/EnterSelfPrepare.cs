using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Net.Contract;

namespace Fineage.Zone.Net;

/// <summary>Heavy self packets built off the simulation thread from stored character data.</summary>
sealed class EnterSelfPrepare
{
    public ItemListNetMsg? Items { get; init; }

    public static EnterSelfPrepare? FromStored(CharacterRecord? stored, ItemCatalog items, ItemObjectIdAllocator ids)
    {
        if (stored?.Items is not { Count: > 0 })
            return null;
        var known = new List<StoredItem>(stored.Items.Count);
        foreach (var row in stored.Items)
        {
            if (items.TryGet(row.ItemId, out _))
            {
                known.Add(row);
                continue;
            }

            ZoneLog.Warn(
                $"inventory drop unknown template char={stored.Id} item={row.ItemId} objectId={row.ObjectId} count={row.Count}");
        }

        if (known.Count == 0)
            return null;
        var inventory = CharacterInventory.Restore(items, ids, known);
        foreach (var collapsed in inventory.CollapsedStacks)
        {
            ZoneLog.Warn(
                $"inventory merge duplicate stack char={stored.Id} item={collapsed.ItemId} droppedObjectId={collapsed.DroppedObjectId} keptObjectId={collapsed.KeptObjectId} count={collapsed.Count}");
        }

        return new EnterSelfPrepare { Items = NetEnterWorldProjector.BuildItems(inventory) };
    }
}