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
            access = DateTimeOffset.UtcNow;
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

        var known = new List<StoredItem>(stored.Items.Count);
        foreach (var row in stored.Items)
        {
            if (_game.Items.TryGet(row.ItemId, out _))
            {
                known.Add(row);
                continue;
            }

            ZoneLog.Warn(
                $"inventory drop unknown template char={session.ObjectId} item={row.ItemId} objectId={row.ObjectId} count={row.Count}");
        }

        var inventory = CharacterInventory.Restore(_game.Items, _itemIds, known);
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
        || _dirtyOfflineFreight.Count > 0;

    /// <summary>
    /// Commits every dirty economic container with the pending ledger entries. <paramref name="discardWithoutPublish"/>
    /// is a character who is leaving: the bag is saved and the journal is dropped, with no InventoryUpdate.
    /// The dirty maps are stolen under <see cref="_economyLock"/> and the SQL runs after the lock is released, so a
    /// logout commit cannot pin the simulation step's flush behind that write.
    /// </summary>
    bool TryCommitDirtyEconomy(int? discardWithoutPublish) =>
        TryCommitDirtyEconomy(discardWithoutPublish, heldReplies: null);

    bool TryCommitDirtyEconomy(
        int? discardWithoutPublish,
        IReadOnlyList<ZoneCommandIngress.Applied>? heldReplies)
    {
        EconomyPersistBatch stolen;
        using (_economyLock.Enter())
        {
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
            RestoreDirtyEconomy);

        // A character with a durable commit still in flight cannot start another. Off the step the caller has to
        // see its own write land, so it waits for that batch and retries instead of reporting a lost commit;
        // that batch published the earlier state, and the retry carries the newer one.
        if (result is EconomyFlushResult.ReservationConflict && !_stepping)
        {
            if (!_economyPersist.WaitForAllBatches(TimeSpan.FromSeconds(10), this))
                LogEconomyCommitFailure(stolen);
            else
            {
                result = _economyPersist.TryEnqueue(
                    stolen,
                    discardWithoutPublish,
                    heldReplies,
                    RestoreDirtyEconomy);
            }
        }

        if (result is EconomyFlushResult.QueueFull or EconomyFlushResult.ReservationConflict)
        {
            LogEconomyCommitFailure(stolen);
            return false;
        }

        // Off the step the caller must return with its write durable (shutdown drain, TryFlushLedger, a request path).
        // Inside a step the batch stays on the worker queue: RunStep drains the completion at its boundaries and the
        // step never waits on Postgres.
        if (!_stepping && result is EconomyFlushResult.Queued)
        {
            // The drain publishes the committed bags, which is world state, so it runs with the turn. Taking the turn
            // here is safe on a caller that already holds it (a monitor is reentrant for its owner) and on a bare
            // shutdown/test thread that does not.
            bool durable;
            using (_affinity.Step())
                durable = AwaitEconomyPersistOffStepBlocking();
            if (!durable)
            {
                LogEconomyCommitFailure(stolen);
                return false;
            }
        }

        return true;
    }

    internal void PublishStolenEconomyInternal(EconomyPersistBatch stolen, int? discardWithoutPublish) =>
        PublishStolenEconomy(stolen, discardWithoutPublish);

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
        stolen.OfflineFreight.AddRange(_dirtyOfflineFreight);
        _dirtyInventories.Clear();
        _dirtyWarehouses.Clear();
        _dirtyFreight.Clear();
        _dirtyOfflineFreight.Clear();
        stolen.Bags = BagsOf(stolen);
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
            if (_dirtyOfflineFreight.Count == 0 && stolen.OfflineFreight.Count > 0)
                _dirtyOfflineFreight.AddRange(stolen.OfflineFreight);
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

        foreach (var extra in stolen.OfflineFreight)
        {
            if (extra.Freight == null)
                continue;
            BagFor(byId, extra.Account, extra.CharacterId).Freight = extra.Freight;
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
