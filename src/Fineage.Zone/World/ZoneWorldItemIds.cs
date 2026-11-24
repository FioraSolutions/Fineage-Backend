using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    internal int NextItemObjectIdForTests => _itemIds.PeekNext();

    /// <summary>Item ids reserved per store round-trip; the next block is prefetched at half use.</summary>
    internal const int ItemIdBlockSize = 4096;

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

        // Shared-store blocks: login and other zones allocate from the same durable counter (no cross-process collisions).
        return new ItemObjectIdAllocator(accounts.ReserveItemObjectIds, ItemIdBlockSize, ledgerFloor);
    }
}