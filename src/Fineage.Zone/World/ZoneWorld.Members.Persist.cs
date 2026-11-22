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

        // Drop unknown templates with a log — Restore already skips them so EnterWorld stays up.
        foreach (var row in stored.Items)
        {
            if (!_game.Items.TryGet(row.ItemId, out _))
                ZoneLog.Info($"inventory skip unknown item={row.ItemId} object={row.ObjectId} char={session.ObjectId}");
        }
        return CharacterInventory.Restore(_game.Items, _itemIds, stored.Items);
    }

    void SaveInventory(ZoneSession session, PlayerInventory inventory)
    {
        // Mobius PlayerInventory.refreshWeight() → _owner.refreshOverloaded(): every inventory write funnels through
        // here, so the overload level is recomputed exactly when the load changes (pickup, drop, shop, trade,
        // warehouse, enchant, equip/unequip, quest reward, sweep, shots).
        RefreshWeightPenaltyAfterInventoryChange(session);
        if (InventoryPersistDeferred)
        {
            _dirtyInventories[session.ObjectId] = (session, inventory);
            return;
        }

        _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, inventory);
    }

    /// <summary>
    /// Ledger first, then inventory, warehouse and freight. False means a commit failed and the rows that did not
    /// land stay dirty; callers must not report economic success for those characters.
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
            CommitStore("inventory replace", pair.Session, () =>
                _persist.ReplaceItemsFromInventory(pair.Session.Account, pair.Session.ObjectId, pair.Inventory)));
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
        {
            FlushDirtyInventories();
            AwaitTestFanoutDelivery();
        }

        return result;
    }
}
