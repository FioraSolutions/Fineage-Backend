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
        lock (_economyLock)
        {
            _dirtyInventories[session.ObjectId] = (session, inventory);
            if (InventoryPersistDeferred)
                return;

            // Economic item rows stay on this caller, in the same transaction as the ledger.
            // InventoryUpdate is sent only after that commit returns.
            TryCommitDirtyEconomy(discardWithoutPublish: null);
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
    bool FlushDirtyInventories()
    {
        lock (_economyLock)
        {
            if (!HasDirtyEconomy())
                return true;
            return TryCommitDirtyEconomy(discardWithoutPublish: null);
        }
    }

    bool HasDirtyEconomy() =>
        _ledger.PendingFlushCount > 0
        || _dirtyInventories.Count > 0
        || _dirtyWarehouses.Count > 0
        || _dirtyFreight.Count > 0
        || _dirtyOfflineFreight.Count > 0;

    /// <summary>
    /// Commits every dirty economic container with the pending ledger entries. <paramref name="discardWithoutPublish"/>
    /// is a character who is leaving: the bag is saved and the journal is dropped, with no InventoryUpdate.
    /// </summary>
    bool TryCommitDirtyEconomy(int? discardWithoutPublish)
    {
        lock (_economyLock)
            return CommitDirtyEconomy(discardWithoutPublish);
    }

    bool CommitDirtyEconomy(int? discardWithoutPublish)
    {
        var bags = CollectDirtyBags();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            _journal.CommitWith(entries =>
            {
                if (_accounts == null)
                    return;
                _accounts.CommitEconomy(entries, bags);
            });
        }
        catch (Exception ex)
        {
            int pending = _ledger.PendingFlushCount;
            ZoneLog.Warn($"auditable economy commit failed pending={pending}: {ex.GetType().Name}: {ex.Message}");
            FineageTelemetry.LedgerFlushFailures.Add(1, new KeyValuePair<string, object?>("pending", pending));
            foreach (var bag in bags)
            {
                int items = bag.Inventory?.Count ?? bag.Warehouse?.Count ?? bag.Freight?.Count ?? 0;
                ZoneLog.Warn(
                    $"inventory persist failed char={bag.CharacterId} account={bag.Account} items={items}: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
        finally
        {
            _syncIo.Record(
                "IAccountStore.CommitEconomy",
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        var published = new List<int>();
        foreach (var (objectId, pair) in _dirtyInventories)
        {
            try
            {
                if (discardWithoutPublish == objectId)
                    pair.Inventory.DiscardChanges();
                else
                    PublishCommittedInventory(pair.Session, pair.Inventory);
                published.Add(objectId);
            }
            catch (Exception ex)
            {
                ZoneLog.Warn(
                    $"inventory publish failed char={objectId} account={pair.Session.Account} items={pair.Inventory.Items.Count}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (int objectId in published)
            _dirtyInventories.Remove(objectId);
        _dirtyWarehouses.Clear();
        _dirtyFreight.Clear();
        _dirtyOfflineFreight.Clear();
        return true;
    }

    List<EconomyBagWrite> CollectDirtyBags()
    {
        var byId = new Dictionary<int, EconomyBagWrite>();
        foreach (var (objectId, pair) in _dirtyInventories)
        {
            byId[objectId] = new EconomyBagWrite(pair.Session.Account, objectId)
            {
                Inventory = CharacterInventory.Rows(pair.Inventory),
            };
        }

        foreach (var (objectId, player) in _dirtyWarehouses)
        {
            if (player.Session == null || player.Warehouse == null)
                continue;
            BagFor(byId, player.Session.Account, objectId).Warehouse = WarehouseRows(player.Warehouse);
        }

        foreach (var (objectId, player) in _dirtyFreight)
        {
            if (player.Session == null || player.Freight == null)
                continue;
            BagFor(byId, player.Session.Account, objectId).Freight = WarehouseRows(player.Freight);
        }

        foreach (var extra in _dirtyOfflineFreight)
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
    /// wrapping a world method that defers too): only the outermost one flushes.
    /// </summary>
    T WithDeferredInventoryPersist<T>(Func<T> change)
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
            AwaitTestFanoutDelivery();
        }

        return result;
    }
}
