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
            _dirtyInventories[objectId] = (session, live.Inventory);
        if (!TryCommitDirtyEconomy(discardWithoutPublish: objectId))
        {
            ZoneLog.Warn($"player persist blocked object {objectId}: auditable economy commit failed");
            return false;
        }

        live.Inventory?.DiscardChanges();
        return true;
    }

    void ClearDeferredPersistRows(int objectId)
    {
        _dirtyInventories.Remove(objectId);
        _dirtyWarehouses.Remove(objectId);
        _dirtyFreight.Remove(objectId);
        _lastPresenceSaveMs.TryRemove(objectId, out _);
        _pendingPresenceAccess.TryRemove(objectId, out _);
    }

    bool PersistInventoryNow(ZoneSession session, PlayerInventory inventory)
    {
        RefreshWeightPenaltyAfterInventoryChange(session);
        _dirtyInventories[session.ObjectId] = (session, inventory);
        if (!TryCommitDirtyEconomy(discardWithoutPublish: session.ObjectId))
            return false;
        inventory.DiscardChanges();
        return true;
    }

    void ReloadCharacterRecord(ZoneSession session, ref CharacterRecord? stored)
    {
        if (_accounts == null || session.ObjectId <= 0)
            return;
        stored = _accounts.FindById(session.Account, session.ObjectId);
    }

    /// <summary>
    /// Waits until this character's snapshots and every pet snapshot have been written. False when that wait times out;
    /// the timeout itself is logged by the worker.
    /// </summary>
    bool DrainCharacterSnapshots(int objectId) =>
        _snapshots.DrainOwner(CharacterPersistWorker.CharacterDomain, objectId, TimeSpan.FromSeconds(2))
        && _snapshots.DrainDomain(CharacterPersistWorker.PetDomain, TimeSpan.FromSeconds(2));

    /// <summary>Logout / evict: write this character and any pet snapshots on the caller. Failures stay queued.</summary>
    bool FlushCharacterSnapshots(int objectId) =>
        _snapshots.FlushSync(CharacterPersistWorker.CharacterDomain, objectId)
        && _snapshots.FlushSync(CharacterPersistWorker.PetDomain, ownerId: null);

    bool CharacterStoreMoved(int objectId, long characterEpoch, long petEpoch) =>
        _snapshots.OwnerEpoch(CharacterPersistWorker.CharacterDomain, objectId) != characterEpoch
        || _snapshots.HasPending(CharacterPersistWorker.CharacterDomain, objectId)
        || _snapshots.DomainEpoch(CharacterPersistWorker.PetDomain) != petEpoch
        || _snapshots.HasPendingDomain(CharacterPersistWorker.PetDomain);

    /// <summary>Flush + reload durable state, then detach without double-writing the same snapshot.</summary>
    bool TryPrepareSameSessionReenter(
        ZoneSession session,
        ref CharacterRecord? stored,
        long characterEpoch,
        long petEpoch)
    {
        if (!session.InWorld)
        {
            if (!CharacterStoreMoved(session.ObjectId, characterEpoch, petEpoch))
                return true;
            if (!DrainCharacterSnapshots(session.ObjectId))
                return false;
            ReloadCharacterRecord(session, ref stored);
            return true;
        }
        if (!TryGetLivePlayer(session, out var live))
        {
            ZoneLog.Warn($"same-session re-enter aborted object {session.ObjectId}: no live player for bound session");
            session.InWorld = false;
            return false;
        }

        if (!TryFlushPlayerPersistBarrier(session.ObjectId, session, live))
            return false;
        // Non-economic snapshots are written off the simulation thread. Reload must see the latest of them.
        if (!DrainCharacterSnapshots(session.ObjectId))
            return false;
        ReloadCharacterRecord(session, ref stored);
        if (!TryDetachPlayer(session, capturePersist: false, out _, out _))
            return false;
        ClearDeferredPersistRows(session.ObjectId);
        return true;
    }

    internal bool HasDeferredInventoryForTests(int objectId) => _dirtyInventories.ContainsKey(objectId);

    internal bool FlushDirtyInventoriesForTests() => FlushDirtyInventories();

    internal void StartCharacterSnapshotsForTests() => _snapshots.Start();

    internal void PauseCharacterSnapshotsForTests() => _snapshots.PauseForTests();

    internal void ResumeCharacterSnapshotsForTests() => _snapshots.ResumeForTests();

    internal void DrainCharacterSnapshotsForTests() => _snapshots.Drain(TimeSpan.FromSeconds(5));

    internal void StopCharacterSnapshotsForTests() => _snapshots.Stop();

    internal int CharacterSnapshotWriterThreadForTests => _snapshots.LastWriterThreadId;

    internal void StageDeferredInventoryForTest(ZoneSession session, PlayerInventory inventory) =>
        _dirtyInventories[session.ObjectId] = (session, inventory);
}