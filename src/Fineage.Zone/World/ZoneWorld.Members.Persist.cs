using Fineage.Protocol.Telemetry;
using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Drops;
using Fineage.Zone.Bosses;
using Fineage.Game.Shops;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Shops;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.Siege;
using Fineage.Zone.Olympiad;
using Fineage.Game.Services;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    ClassSpawn.Point SpawnFor(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored is { HasPosition: true })
            return new ClassSpawn.Point(stored.X, stored.Y, stored.Z);

        if (!PlayerClasses.TryGet(session.ClassId, out var template) || template == null || template.Spawns.Count == 0)
            return new ClassSpawn.Point(TalkingIslandSpawn.X, TalkingIslandSpawn.Y, TalkingIslandSpawn.Z);
        var spawns = template.Spawns;
        int idx = _spawnRandom.For(_regions.RegionOf(0, spawns[0].X, spawns[0].Y)).Next(spawns.Count);
        var picked = spawns[idx];
        return new ClassSpawn.Point(picked.X, picked.Y, picked.Z);
    }

    ClassCatalog PlayerClasses => _game.Classes ?? ClassTemplates.Catalog;

    (float run, float walk) MoveSpeed(int classId)
    {
        if (PlayerClasses.TryGet(classId, out var template) && template != null)
            return (template.RunSpeed, template.WalkSpeed);
        return (120f, 80f);
    }

    /// <summary>Mobius <c>getDEX()</c> from the class template — drives the <c>FuncMoveSpeed</c> speed multiplier.</summary>
    int DexOf(int classId) =>
        PlayerClasses.TryGet(classId, out var template) && template != null ? template.Dex : 30;

    const long PresenceSaveIntervalMs = 3000;
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _lastPresenceSaveMs = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTimeOffset> _pendingPresenceAccess = new();

    /// <summary>Throttled save of the move destination so a Zone restart mid-session keeps the player's spot.</summary>
    void MaybeSavePresenceOnMove(ZoneSession session)
    {
        if (!_persist.Enabled || session.ObjectId <= 0)
            return;
        long now = NowUnixMs();
        long last = _lastPresenceSaveMs.GetValueOrDefault(session.ObjectId, long.MinValue / 2);
        if (now - last < PresenceSaveIntervalMs)
            return;
        _lastPresenceSaveMs[session.ObjectId] = now;
        SavePresence(session, session.X, session.Y, session.Z, touchAccess: false);
    }

    void SavePresence(ZoneSession session, int x, int y, int z, bool touchAccess)
    {
        // A presence snapshot replaces any older one of the same kind, so it has to carry the access time too.
        // A later move that omitted it would drop the enter's last-access write.
        DateTimeOffset? access = null;
        if (touchAccess)
        {
            // Last-access is account metadata, not a gameplay timer: wall-only.
            access = _clock.UtcNow;
            _pendingPresenceAccess[session.ObjectId] = access.Value;
        }
        else if (_pendingPresenceAccess.TryGetValue(session.ObjectId, out var known))
            access = known;

        _persist.SavePresence(session.Account, session.ObjectId, x, y, z, access);
    }

    PlayerInventory LoadInventory(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored == null || stored.Items == null)
            return new PlayerInventory(_game.Items, _itemIds);

        // StoredStateAudit.RequireLoadable refused the enter already when a row names a missing template.
        foreach (var row in stored.Items)
            _game.Items.Require(row.ItemId);

        var inventory = CharacterInventory.Restore(_game.Items, _itemIds, stored.Items);
        foreach (var collapsed in inventory.CollapsedStacks)
        {
            ZoneLog.Warn(
                $"inventory merge duplicate stack char={session.ObjectId} item={collapsed.ItemId} droppedObjectId={collapsed.DroppedObjectId} keptObjectId={collapsed.KeptObjectId} count={collapsed.Count}");
        }

        return inventory;
    }

    void SaveInventory(ZoneSession session, PlayerInventory inventory)
    {
        // Mobius PlayerInventory.refreshWeight() → _owner.refreshOverloaded(): every inventory write funnels through
        // here, so the overload level is recomputed exactly when the load changes (pickup, drop, shop, trade,
        // warehouse, enchant, equip/unequip, quest reward, sweep, shots).
        RefreshWeightPenaltyAfterInventoryChange(session);
        bool commitNow;
        using (_economyLock.Enter())
        {
            _dirtyInventories[session.ObjectId] = (session, inventory);
            if (_stepping)
                _economyTouched.Add(session.ObjectId);
            commitNow = !InventoryPersistDeferred;
        }

        // Economic item rows stay on this caller, in the same transaction as the ledger.
        // InventoryUpdate is sent only after that commit returns. The write itself is outside the lock:
        // logout commits must not hold it across SQL while the simulation step's flush waits for it.
        if (commitNow)
        {
            TryCommitDirtyEconomy(discardWithoutPublish: null);
            // Inside a step the batch stays on the worker queue: RunStep drains it at its boundaries and the step
            // must never wait on Postgres. Off the step the caller has to return with its write durable.
            if (!InSimulationStep)
                AwaitEconomyPersistOffStep();
        }
    }

    /// <summary>One InventoryUpdate for every journal row, after the bag write has succeeded.</summary>
    void PublishCommittedInventory(ZoneSession session, PlayerInventory inventory)
    {
        var changes = inventory.DrainChanges();
        if (changes.Count == 0 || NetBridge == null)
            return;
        int version = inventory.BumpVersion();
        NetBridge.FanoutInventoryUpdate(
            [session.ObjectId],
            InventoryMessages.FromPending(changes, version));
    }

    /// <summary>
    /// The pending ledger rows and every dirty bag, warehouse, and freight hold commit in one transaction on this
    /// caller. False leaves those rows dirty and does not publish. InventoryUpdate goes out only after the commit
    /// returns, and only for a bag that is still in the world.
    /// </summary>
    bool FlushDirtyInventories() => TryCommitDirtyEconomy(discardWithoutPublish: null);

    bool HasDirtyEconomy() =>
        _ledger.PendingFlushCount > 0
        || _dirtyInventories.Count > 0
        || _dirtyWarehouses.Count > 0
        || _dirtyFreight.Count > 0
        || _dirtyOfflineBags.Count > 0
        || _worldState.HasDirty;

    /// <summary>
    /// Commits every dirty economic container with the pending ledger entries. <paramref name="discardWithoutPublish"/>
    /// is a character who is leaving: the bag is saved and the journal is dropped, with no InventoryUpdate.
    /// The dirty maps are stolen under <see cref="_economyLock"/> and the SQL runs after the lock is released, so a
    /// logout commit cannot pin the simulation step's flush behind that write.
    /// </summary>
    bool TryCommitDirtyEconomy(int? discardWithoutPublish) =>
        TryCommitDirtyEconomy(discardWithoutPublish, heldReplies: null, out _, out _, out _);

    /// <param name="repliesHeld">
    /// True only when <paramref name="heldReplies"/> rode on a batch that was queued: that batch's completion answers
    /// them. Otherwise (no dirty economy, queue full, reservation conflict) the caller still owns every reply.
    /// </param>
    /// <param name="ownReplies">
    /// <paramref name="heldReplies"/> without <paramref name="deferred"/>: the replies whose outcome this call decides.
    /// </param>
    /// <param name="deferred">
    /// Replies of characters whose economy was already taken by another commit that is not durable yet (#1114: a
    /// logout steals every dirty bag, so the step that applied the change finds nothing of it to commit). They must not
    /// hear success from this call: neither "nothing to commit" nor this step's batch makes that change durable. The
    /// caller carries them until that commit finishes. Decided under the same <see cref="_economyLock"/> hold as this
    /// call's own steal, so no other commit can take the change in between.
    /// </param>
    bool TryCommitDirtyEconomy(
        int? discardWithoutPublish,
        IReadOnlyList<ZoneCommandIngress.Applied>? heldReplies,
        out bool repliesHeld,
        out IReadOnlyList<ZoneCommandIngress.Applied>? ownReplies,
        out List<ZoneCommandIngress.Applied>? deferred)
    {
        repliesHeld = false;
        ownReplies = heldReplies;
        deferred = null;
        EconomyPersistBatch stolen;
        using (_economyLock.Enter())
        {
            if (heldReplies != null)
            {
                if (heldReplies.Count > 0 && _economyInFlight.Count > 0)
                {
                    deferred = SplitRepliesOnOtherCommits(heldReplies, out var own);
                    if (deferred != null)
                    {
                        ownReplies = own;
                        heldReplies = own;
                    }
                }

                // The step's commit is the boundary: what its commands touched is now either in its batch or deferred.
                _economyTouched.Clear();
            }

            if (!HasDirtyEconomy())
                return true;
            stolen = StealDirtyEconomy();
        }

        if (_stepping)
            NotePhase("economy-commit");

        if (_economyPersist == null)
            throw new InvalidOperationException("economy persist service is not configured");

        var result = _economyPersist.TryEnqueue(
            stolen,
            discardWithoutPublish,
            heldReplies,
            RestoreDirtyEconomy,
            out var finished);

        // A character with a durable commit still in flight cannot start another. Off the step the caller has to
        // see its own write land, so it waits for the batches holding those characters and retries instead of
        // reporting a lost commit; that batch published the earlier state, and the retry carries the newer one.
        if (result is EconomyFlushResult.ReservationConflict && !_stepping)
        {
            if (!_economyPersist.WaitForReservations(
                    EconomyPersistService.CharacterIdsOfBatch(stolen), TimeSpan.FromSeconds(10), this))
                LogEconomyCommitFailure(stolen);
            else
            {
                // The conflict restored the batch into the dirty maps. Steal again rather than re-sending the old batch:
                // re-sending left those restored rows dirty as well, so a relog saw this character's economy as
                // still pending until some later step wrote the same rows a second time (#1116).
                using (_economyLock.Enter())
                {
                    if (!HasDirtyEconomy())
                        return true;
                    stolen = StealDirtyEconomy();
                }

                result = _economyPersist.TryEnqueue(
                    stolen,
                    discardWithoutPublish,
                    heldReplies,
                    RestoreDirtyEconomy,
                    out finished);
            }
        }

        if (result is EconomyFlushResult.QueueFull or EconomyFlushResult.ReservationConflict)
        {
            // Inside a step the batch is restored dirty and the next step commits it; its replies wait for that commit
            // (ZoneWorldEconomyCarry), so nothing failed yet. Off the step the caller needed the write now.
            if (!_stepping)
                LogEconomyCommitFailure(stolen);
            return false;
        }

        repliesHeld = result is EconomyFlushResult.Queued && heldReplies is { Count: > 0 };

        // Off the step the caller must return with its write durable (shutdown drain, TryFlushLedger, a request path).
        // Inside a step the batch stays on the worker queue: RunStep drains the completion at its boundaries and the
        // step never waits on Postgres. Off the step the wait is for this batch only, and the world turn is held only
        // while completions drain: a logout that held the turn across a wait for every batch in flight froze the
        // simulation for up to the 30 s budget while a reconnect storm kept queueing logout batches (#990).
        if (!_stepping && result is EconomyFlushResult.Queued && finished != null
            && !_economyPersist.WaitForBatch(finished, TimeSpan.FromSeconds(30), this))
        {
            LogEconomyCommitFailure(stolen);
            return false;
        }

        return true;
    }

    internal void PublishStolenEconomyInternal(EconomyPersistBatch stolen, int? discardWithoutPublish)
    {
        PublishStolenEconomy(stolen, discardWithoutPublish);
        _worldState.Committed(stolen.WorldState);
        using (_economyLock.Enter())
            EndInFlightLocked(stolen);
    }

    /// <summary>
    /// Characters whose economy sits in a stolen batch that has not finished yet (queued, being written, or about to be
    /// handed to the worker). Guarded by <see cref="_economyLock"/>; a count because a restored batch can be re-taken
    /// while an older one for the same character is still being decided.
    /// </summary>
    readonly Dictionary<int, int> _economyInFlight = new();

    /// <summary>
    /// Characters whose bag, warehouse, or freight a simulation step saved since that step's commit. Guarded by
    /// <see cref="_economyLock"/>. Only these replies can depend on a change another commit took; a character whose
    /// earlier economy is still in flight but whose command this step changed nothing economic answers now (#990).
    /// </summary>
    readonly HashSet<int> _economyTouched = [];

    /// <summary>Tests: whether a stolen, not yet durable batch holds this character's economy.</summary>
    internal bool IsEconomyInFlightForTests(int characterId)
    {
        using (_economyLock.Enter())
            return _economyInFlight.ContainsKey(characterId);
    }

    /// <summary>Caller holds <see cref="_economyLock"/>.</summary>
    List<ZoneCommandIngress.Applied>? SplitRepliesOnOtherCommits(
        IReadOnlyList<ZoneCommandIngress.Applied> replies,
        out List<ZoneCommandIngress.Applied> own)
    {
        List<ZoneCommandIngress.Applied>? deferred = null;
        own = new List<ZoneCommandIngress.Applied>(replies.Count);
        foreach (var row in replies)
        {
            // A command that failed changed nothing, so its refusal is true whatever the economy does.
            int id = row.Submission.Session.ObjectId;
            bool dependsOnEconomy = _economyTouched.Contains(id) || _economyCarriedSince.ContainsKey(row.Submission);
            if (row.Error == null && row.Failure == null && dependsOnEconomy && _economyInFlight.ContainsKey(id))
                (deferred ??= []).Add(row);
            else
                own.Add(row);
        }

        return deferred;
    }

    /// <summary>Caller holds <see cref="_economyLock"/>. Exactly once per steal: publish or restore ends it.</summary>
    void EndInFlightLocked(EconomyPersistBatch batch)
    {
        if (batch.InFlightIds is not { } ids)
            return;
        batch.InFlightIds = null;
        foreach (int id in ids)
        {
            if (_economyInFlight.TryGetValue(id, out int n) && n > 1)
                _economyInFlight[id] = n - 1;
            else
                _economyInFlight.Remove(id);
        }
    }

    internal void RestoreDirtyEconomyInternal(EconomyPersistBatch stolen) => RestoreDirtyEconomy(stolen);

    void LogEconomyCommitFailure(EconomyPersistBatch stolen, Exception? ex = null)
    {
        int pending = _ledger.PendingFlushCount;
        if (ex != null)
        {
            ZoneLog.Warn($"auditable economy commit failed pending={pending}: {ex.GetType().Name}: {ex.Message}");
            FineageTelemetry.LedgerFlushFailures.Add(1, new KeyValuePair<string, object?>("pending", pending));
        }

        foreach (var bag in stolen.Bags)
        {
            int items = bag.Inventory?.Count ?? bag.Warehouse?.Count ?? bag.Freight?.Count ?? 0;
            ZoneLog.Warn(
                $"inventory persist failed char={bag.CharacterId} account={bag.Account} items={items}" +
                (ex != null ? $": {ex.GetType().Name}: {ex.Message}" : ": worker returned false"));
        }
    }

    void PublishStolenEconomy(EconomyPersistBatch stolen, int? discardWithoutPublish)
    {
        foreach (var (objectId, session, inventory) in stolen.Inventories)
        {
            try
            {
                if (discardWithoutPublish == objectId)
                    inventory.DiscardChanges();
                else
                    PublishCommittedInventory(session, inventory);
            }
            catch (Exception ex)
            {
                ZoneLog.Warn(
                    $"inventory publish failed char={objectId} account={session.Account} items={inventory.Items.Count}: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    EconomyPersistBatch StealDirtyEconomy()
    {
        var stolen = new EconomyPersistBatch();
        foreach (var (objectId, pair) in _dirtyInventories)
            stolen.Inventories.Add((objectId, pair.Session, pair.Inventory));
        foreach (var (objectId, player) in _dirtyWarehouses)
            stolen.Warehouses.Add((objectId, player));
        foreach (var (objectId, player) in _dirtyFreight)
            stolen.Freight.Add((objectId, player));
        stolen.OfflineBags.AddRange(_dirtyOfflineBags);
        _dirtyInventories.Clear();
        _dirtyWarehouses.Clear();
        _dirtyFreight.Clear();
        _dirtyOfflineBags.Clear();
        stolen.Bags = BagsOf(stolen);
        stolen.WorldState = _worldState.Steal();
        // Taken with the bags, under the economy lock: the rows up to here are exactly the moves these bags show.
        stolen.LedgerThrough = _ledger.Position;
        stolen.InFlightIds = EconomyPersistService.CharacterIdsOfBatch(stolen);
        foreach (int id in stolen.InFlightIds)
            _economyInFlight[id] = _economyInFlight.GetValueOrDefault(id) + 1;
        return stolen;
    }

    void RestoreDirtyEconomy(EconomyPersistBatch stolen)
    {
        using (_economyLock.Enter())
        {
            foreach (var (objectId, session, inventory) in stolen.Inventories)
                _dirtyInventories.TryAdd(objectId, (session, inventory));
            foreach (var (objectId, player) in stolen.Warehouses)
                _dirtyWarehouses.TryAdd(objectId, player);
            foreach (var (objectId, player) in stolen.Freight)
                _dirtyFreight.TryAdd(objectId, player);
            // Stolen writes are older than anything queued since: they go first so a later snapshot of the same
            // character still wins in BagsOf, and a different character's write is never dropped.
            _dirtyOfflineBags.InsertRange(0, stolen.OfflineBags);
            _worldState.Restore(stolen.WorldState);
            // Dirty again in the same lock hold, so a reply check never sees the change as neither dirty nor in flight.
            EndInFlightLocked(stolen);
        }
    }

    static List<EconomyBagWrite> BagsOf(EconomyPersistBatch stolen)
    {
        var byId = new Dictionary<int, EconomyBagWrite>();
        foreach (var (objectId, session, inventory) in stolen.Inventories)
        {
            byId[objectId] = new EconomyBagWrite(session.Account, objectId)
            {
                Inventory = CharacterInventory.Rows(inventory),
            };
        }

        foreach (var (objectId, player) in stolen.Warehouses)
        {
            if (player.Session == null || player.Warehouse == null)
                continue;
            BagFor(byId, player.Session.Account, objectId).Warehouse = WarehouseRows(player.Warehouse);
        }

        foreach (var (objectId, player) in stolen.Freight)
        {
            if (player.Session == null || player.Freight == null)
                continue;
            BagFor(byId, player.Session.Account, objectId).Freight = WarehouseRows(player.Freight);
        }

        foreach (var extra in stolen.OfflineBags)
        {
            var bag = BagFor(byId, extra.Account, extra.CharacterId);
            if (extra.Inventory != null)
                bag.Inventory = extra.Inventory;
            if (extra.Freight != null)
                bag.Freight = extra.Freight;
        }

        return byId.Values.ToList();
    }

    static EconomyBagWrite BagFor(Dictionary<int, EconomyBagWrite> byId, string account, int objectId)
    {
        if (!byId.TryGetValue(objectId, out var bag))
            byId[objectId] = bag = new EconomyBagWrite(account, objectId);
        return bag;
    }

    static StoredItem[] WarehouseRows(PrivateWarehouse warehouse)
    {
        var stacks = warehouse.Items;
        var stored = new StoredItem[stacks.Count];
        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            stored[i] = new StoredItem(stack.ItemId, stack.Count, -1, stack.EnchantLevel, stack.ObjectId);
        }

        return stored;
    }

    /// <summary>
    /// Runs <paramref name="change"/> with inventory/warehouse Postgres deferred until it returns. Calls nest (a command
    /// wrapping a world method that defers too): only the outermost one flushes. It also owns the world turn for the
    /// whole call: the flush waits for the durable commit, which releases the turn, and the body reads world state.
    /// </summary>
    T WithDeferredInventoryPersist<T>(Func<T> change)
    {
        if (!InSimulationStep && _affinity.MayTouch)
            return WithDeferredInventoryPersistUnderTurn(change);

        using (_affinity.Step())
            return WithDeferredInventoryPersistUnderTurn(change);
    }

    T WithDeferredInventoryPersistUnderTurn<T>(Func<T> change)
    {
        T result;
        bool outermost = _deferInventoryPersistDepth == 0;
        _deferInventoryPersistDepth++;
        try
        {
            result = change();
        }
        finally
        {
            _deferInventoryPersistDepth--;
        }

        if (outermost)
        {
            FlushDirtyInventories();
            // Inside a simulation step the batch stays queued: the step drains completions at its boundaries and
            // must never wait for the durable commit. Off-tick callers wait so their change is durable on return.
            if (!InSimulationStep)
                AwaitEconomyPersistOffStep();
            AwaitTestFanoutDelivery();
        }

        return result;
    }

    /// <summary>
    /// Off-tick API paths wait for queued durable commits on the caller thread. The simulation step never calls this;
    /// it drains completions at step boundaries instead.
    /// </summary>
    void AwaitEconomyPersistOffStep() => _ = AwaitEconomyPersistOffStepBlocking();

    /// <summary>
    /// Waits on any caller. <see cref="ZoneWorldSimulation.RunStep"/> never calls this — it drains completions at
    /// step boundaries instead — and the assert names that caller if a future step path does.
    /// </summary>
    bool AwaitEconomyPersistOffStepBlocking()
    {
        if (_economyPersist == null)
            return true;
        EconomyPersistThreading.AssertNoBlockingWaitOnSimulationThread(InSimulationStep, nameof(AwaitEconomyPersistOffStepBlocking));
        if (!_economyPersist.WaitForAllBatches(TimeSpan.FromSeconds(30), this))
            return false;
        // The caller already holds the turn (it touched the world before it waited); stepping again here would
        // leave the turn unbalanced because WorldAffinity allows one depth for a whole step body.
        _economyPersist.DrainCompletions(this);
        return true;
    }
}
