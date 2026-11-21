using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    internal int NextItemObjectIdForTests => _itemIds.PeekNext();

    static ItemObjectIdAllocator SeedItemObjectIds(IAccountStore? accounts, ItemObjectIdAllocator? injected, EconomyLedger ledger)
    {
        if (injected != null)
            return injected;
        int ledgerFloor = ledger.MaxInstanceId > 0 ? checked(ledger.MaxInstanceId + 1) : PlayerInventory.FirstObjectId;
        if (accounts == null)
        {
            if (ledger.MaxInstanceId > 0)
                throw new InvalidOperationException(
                    "ZoneWorld requires IAccountStore.NextItemObjectId when the economy ledger already records item instances");
            return new ItemObjectIdAllocator();
        }

        int storeNext = accounts.NextItemObjectId;
        int first = Math.Max(storeNext, ledgerFloor);
        if (first < PlayerInventory.FirstObjectId)
            first = PlayerInventory.FirstObjectId;
        return new ItemObjectIdAllocator(first);
    }
}