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
        var picked = template.Spawns[_spawnRandom.Next(template.Spawns.Count)];
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

    /// <summary>Throttled save of the move destination so a Zone restart mid-session keeps the player's spot.</summary>
    void MaybeSavePresenceOnMove(ZoneSession session)
    {
        if (!_persist.Enabled || session.ObjectId <= 0)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        long last = _lastPresenceSaveMs.GetValueOrDefault(session.ObjectId, long.MinValue / 2);
        if (now - last < PresenceSaveIntervalMs)
            return;
        _lastPresenceSaveMs[session.ObjectId] = now;
        SavePresence(session, session.X, session.Y, session.Z, touchAccess: false);
    }

    void SavePresence(ZoneSession session, int x, int y, int z, bool touchAccess) =>
        _persist.SavePresence(
            session.Account,
            session.ObjectId,
            x,
            y,
            z,
            touchAccess ? DateTimeOffset.UtcNow : null);

    PlayerInventory LoadInventory(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);
        // Items == null was backfilled off-lock in TryEnterAsync; treat missing as starter in-memory only.
        if (stored.Items == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);

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
        if (_deferInventoryPersist)
        {
            _dirtyInventories[session.ObjectId] = (session, inventory);
            return;
        }

        _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, inventory);
    }

    /// <summary>Flush inventory/warehouse rows queued while <see cref="_deferInventoryPersist"/> was set. Call outside <c>_gate</c>.</summary>
    void FlushDirtyInventories()
    {
        if (_dirtyInventories.Count > 0)
        {
            foreach (var (_, pair) in _dirtyInventories)
                _persist.ReplaceItemsFromInventory(pair.Session.Account, pair.Session.ObjectId, pair.Inventory);
            _dirtyInventories.Clear();
        }

        if (_dirtyWarehouses.Count == 0)
            return;
        foreach (var player in _dirtyWarehouses.Values)
        {
            if (player.Session == null)
                continue;
            ReplaceWarehouseNow(player.Session, player);
        }

        _dirtyWarehouses.Clear();
    }

    /// <summary>Run <paramref name="underGate"/> under <c>_gate</c> with inventory/warehouse Postgres deferred until after unlock.</summary>
    T WithDeferredInventoryPersist<T>(Func<T> underGate)
    {
        T result;
        lock (_gate)
        {
            _deferInventoryPersist = true;
            try
            {
                result = underGate();
            }
            finally
            {
                _deferInventoryPersist = false;
            }
        }

        FlushDirtyInventories();
        return result;
    }
}
