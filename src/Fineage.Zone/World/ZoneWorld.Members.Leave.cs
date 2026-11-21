using Fineage.Accounts;
using Fineage.Game;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Shared leave / kick / same-session re-enter barrier: auditable economy ledger flush, then durable inventory rows
    /// for <paramref name="objectId"/>, without leaving stale deferred maps.
    /// </summary>
    bool TryFlushPlayerPersistBarrier(int objectId, ZoneSession session, ZoneEntity live)
    {
        if (live.Inventory != null)
            PersistInventoryNow(session, live.Inventory);
        if (_dirtyWarehouses.Remove(objectId, out var warehousePlayer))
            ReplaceWarehouseNow(session, warehousePlayer);
        if (_dirtyFreight.Remove(objectId, out var freightPlayer))
            ReplaceFreightNow(session, freightPlayer);

        if (_ledger.PendingFlushCount > 0 && !TryFlushLedger())
        {
            ZoneLog.Warn($"player persist blocked object {objectId}: auditable economy ledger flush failed");
            return false;
        }

        if (_dirtyInventories.Remove(objectId, out var dirty))
            _persist.ReplaceItemsFromInventory(dirty.Session.Account, dirty.Session.ObjectId, dirty.Inventory);
        return true;
    }

    void ClearDeferredPersistRows(int objectId)
    {
        _dirtyInventories.Remove(objectId);
        _dirtyWarehouses.Remove(objectId);
        _dirtyFreight.Remove(objectId);
        _lastPresenceSaveMs.TryRemove(objectId, out _);
    }

    void PersistInventoryNow(ZoneSession session, PlayerInventory inventory)
    {
        RefreshWeightPenaltyAfterInventoryChange(session);
        _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, inventory);
    }

    void ReloadCharacterRecord(ZoneSession session, ref CharacterRecord? stored)
    {
        if (_accounts == null || session.ObjectId <= 0)
            return;
        stored = _accounts.FindById(session.Account, session.ObjectId);
    }

    /// <summary>Flush + reload durable state, then detach without double-writing the same snapshot.</summary>
    bool TryPrepareSameSessionReenter(ZoneSession session, ref CharacterRecord? stored)
    {
        if (!session.InWorld)
            return true;
        if (!TryGetLivePlayer(session, out var live))
        {
            ZoneLog.Warn($"same-session re-enter aborted object {session.ObjectId}: no live player for bound session");
            session.InWorld = false;
            return false;
        }

        if (!TryFlushPlayerPersistBarrier(session.ObjectId, session, live))
            return false;
        ReloadCharacterRecord(session, ref stored);
        if (!TryDetachPlayer(session, capturePersist: false, out _, out _))
            return false;
        ClearDeferredPersistRows(session.ObjectId);
        return true;
    }

    internal bool HasDeferredInventoryForTests(int objectId) => _dirtyInventories.ContainsKey(objectId);

    internal void FlushDirtyInventoriesForTests() => FlushDirtyInventories();

    internal void StageDeferredInventoryForTest(ZoneSession session, PlayerInventory inventory) =>
        _dirtyInventories[session.ObjectId] = (session, inventory);
}