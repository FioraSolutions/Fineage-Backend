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
        _dirtyInventories[session.ObjectId] = (session, inventory);
        if (InventoryPersistDeferred)
            return;
        FlushDirtyInventories();
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
    /// Ledger first, then inventory, warehouse and freight. False means a commit failed and the rows that did not
    /// land stay dirty; callers must not report economic success for those characters. InventoryUpdate goes out only
    /// for a bag whose snapshot write returned success.
    /// </summary>
    bool FlushDirtyInventories()
    {
        if (!TryFlushLedger())
        {
            ZoneLog.Warn("inventory persist blocked: auditable economy ledger flush failed");
            return false;
        }

        bool committed = true;
        committed &= CommitDirty(_dirtyInventories, (_, pair) =>
        {
            if (!CommitStore("inventory replace", pair.Session, () =>
                    _persist.ReplaceItemsFromInventory(pair.Session.Account, pair.Session.ObjectId, pair.Inventory)))
                return false;
            PublishCommittedInventory(pair.Session, pair.Inventory);
            return true;
        });
        committed &= CommitDirty(_dirtyWarehouses, (_, player) =>
            player.Session == null || ReplaceWarehouseNow(player.Session, player));
        committed &= CommitDirty(_dirtyFreight, (_, player) =>
            player.Session == null || ReplaceFreightNow(player.Session, player));
        return committed;
    }

    /// <summary>
    /// Writes each dirty row. Rows that land are removed; a refusal or a thrown store error stays queued and is logged.
    /// </summary>
    static bool CommitDirty<T>(Dictionary<int, T> dirty, Func<int, T, bool> commit)
    {
        if (dirty.Count == 0)
            return true;
        var done = new List<int>();
        bool ok = true;
        foreach (var (id, row) in dirty)
        {
            if (commit(id, row))
                done.Add(id);
            else
                ok = false;
        }

        foreach (int id in done)
            dirty.Remove(id);
        return ok;
    }

    bool CommitStore(string what, ZoneSession session, Func<bool> write)
    {
        try
        {
            if (write())
                return true;
            ZoneLog.Warn(
                $"{what} refused account={session.Account} character={session.ObjectId}: store returned false");
            return false;
        }
        catch (Exception ex)
        {
            ZoneLog.Warn(
                $"{what} failed account={session.Account} character={session.ObjectId}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
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
            FlushDirtyInventories();
        return result;
    }
}
