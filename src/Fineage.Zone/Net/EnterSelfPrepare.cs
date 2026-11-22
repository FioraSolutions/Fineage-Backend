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
        var inventory = CharacterInventory.Restore(items, ids, stored.Items);
        return new EnterSelfPrepare { Items = NetEnterWorldProjector.BuildItems(inventory) };
    }
}