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
        using (_economyLock.Enter())
        {
            if (live.Inventory != null)
                _dirtyInventories[objectId] = (session, live.Inventory);
        }

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
        using (_economyLock.Enter())
        {
            _dirtyInventories.Remove(objectId);
            _dirtyWarehouses.Remove(objectId);
            _dirtyFreight.Remove(objectId);
        }
        _lastPresenceSaveMs.TryRemove(objectId, out _);
        _pendingPresenceAccess.TryRemove(objectId, out _);
    }

    bool PersistInventoryNow(ZoneSession session, PlayerInventory inventory)
    {
        RefreshWeightPenaltyAfterInventoryChange(session);
        using (_economyLock.Enter())
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
    /// <summary>
    /// True when the off-tick load is still current. A store that moved, or a player still in the world, returns
    /// false without reading the database. <paramref name="reload"/> is true when the caller should load again
    /// off the simulation thread (the player was detached, or the store moved). A failed economy commit sets
    /// <see cref="ZoneSession.EnterRefusal"/> and leaves <paramref name="reload"/> false.
    /// </summary>
    bool TryPrepareSameSessionReenter(ZoneSession session, long characterEpoch, long petEpoch, out bool reload)
    {
        if (!session.InWorld)
        {
            reload = CharacterStoreMoved(session.ObjectId, characterEpoch, petEpoch);
            return !reload;
        }

        if (!TryGetLivePlayer(session, out var live))
        {
            string reason = $"enter refused: character {session.ObjectId} is marked in the world but has no live player";
            session.EnterRefusal = reason;
            ZoneLog.Warn(reason);
            session.InWorld = false;
            reload = false;
            return false;
        }

        if (!TryFlushPlayerPersistBarrier(session.ObjectId, session, live))
        {
            session.EnterRefusal = $"enter refused: character {session.ObjectId} economy commit failed";
            reload = false;
            return false;
        }

        if (!TryDetachPlayer(session, capturePersist: false, out _, out _))
        {
            session.EnterRefusal = $"enter refused: character {session.ObjectId} could not leave before re-enter";
            reload = false;
            return false;
        }

        ClearDeferredPersistRows(session.ObjectId);
        reload = true;
        return false;
    }

    internal bool HasDeferredInventoryForTests(int objectId)
    {
        using (_economyLock.Enter())
            return _dirtyInventories.ContainsKey(objectId);
    }

    internal bool FlushDirtyInventoriesForTests() => FlushDirtyInventories();

    internal void StartCharacterSnapshotsForTests() => _snapshots.Start();

    internal void PauseCharacterSnapshotsForTests() => _snapshots.PauseForTests();

    internal void ResumeCharacterSnapshotsForTests() => _snapshots.ResumeForTests();

    internal void DrainCharacterSnapshotsForTests() => _snapshots.Drain(TimeSpan.FromSeconds(5));

    internal void StopCharacterSnapshotsForTests() => _snapshots.Stop();

    internal int CharacterSnapshotWriterThreadForTests => _snapshots.LastWriterThreadId;

    internal void StageDeferredInventoryForTest(ZoneSession session, PlayerInventory inventory)
    {
        using (_economyLock.Enter())
            _dirtyInventories[session.ObjectId] = (session, inventory);
    }
}