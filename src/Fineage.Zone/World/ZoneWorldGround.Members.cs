using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void GrantInventoryStack(ZoneEntity looter, int itemId, int count, bool notifyObtained)
    {
        if (looter.Inventory == null || looter.Session == null || count <= 0)
            return;
        bool merged = looter.Inventory.FindByItemId(itemId) != null;
        var added = looter.Inventory.Add(itemId, count);
        var invUpdate = InventoryMessages.Update(merged ? InventoryChange.Modify : InventoryChange.Add, added);
        NetBridge?.FanoutInventoryUpdate([looter.Session.ObjectId], invUpdate);
        SaveInventory(looter.Session, looter.Inventory);
        if (notifyObtained)
            NotifyObtained(looter.Session, itemId, count);
    }

    void NotifyObtained(ZoneSession session, int itemId, int count)
    {
        _game.Items.TryGet(itemId, out var template);
        string name = template?.Name ?? $"item {itemId}";
        if (itemId == WarehouseRules.AdenaItemId)
            ShotNotice(session, SysMsgObtainedAdena, $"You have obtained {count} adena.");
        else if (count > 1)
            ShotNotice(session, SysMsgObtainedCount, $"You have obtained {count} {name}.");
        else
            ShotNotice(session, SysMsgObtainedItem, $"You have obtained {name}.");
    }

    void PickupFailed(ZoneSession session, ZoneEntity ground)
    {
        session.PendingPickupId = 0;
        session.PendingPickupAtUnixMs = 0;
        string name = _game.Items.TryGet(ground.GroundItemId, out var t) ? t.Name : $"item {ground.GroundItemId}";
        ShotNotice(session, SysMsgFailedToPickup, $"You have failed to pick up {name}.");
    }

    void TickPendingPickups(long now)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || session.PendingPickupId <= 0 || now < session.PendingPickupAtUnixMs)
                continue;
            int id = session.PendingPickupId;
            session.PendingPickupId = 0;
            session.PendingPickupAtUnixMs = 0;
            // Item may be gone (someone else took it) — that is a silent no-op like Mobius.
            if (_byId.TryGetValue(id, out var g) && g.Kind == ZoneEntityKind.GroundItem)
                TryPickup(session, id, allowApproach: false);
        }
    }

    /// <summary>
    /// Mobius <c>RequestDestroyItem</c> body. It shares the drop path's guards (owner, not equipped, not a
    /// quest item, count available) but never spawns a ground stack and never checks distance.
    /// </summary>
    string? TryDestroyItem(ZoneSession session, int objectId, int count)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (objectId <= 0 || count < 1)
            return "bad_request";

        var item = player.Inventory.Find(objectId);
        if (item == null || item.IsEquipped)
            return InventoryProtocol.NotOwned;
        if (!item.Template.CanDropTradeStore)
            return "bad_request"; // Mobius: quest items cannot be destroyed either
        if (item.Count < count)
            return "bad_request";

        if (!player.Inventory.TryReduce(item, count, out bool removedEntirely))
            return "bad_request";

        var invUpdate = removedEntirely
            ? new InventoryUpdateMsg { Changes = [InventoryMessages.RemoveRow(item, -1)] }
            : InventoryMessages.Update(InventoryChange.Modify, item);
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], invUpdate);
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"destroy-bag object={session.ObjectId} item={item.ItemId} x{count} all={removedEntirely}");
        return null;
    }

    string? TryDropItem(ZoneSession session, int objectId, int count, int x, int y, int z)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (objectId <= 0 || count < 1)
            return "bad_request";

        var item = player.Inventory.Find(objectId);
        if (item == null || item.IsEquipped)
            return InventoryProtocol.NotOwned;
        if (!item.Template.CanDropTradeStore)
            return "bad_request"; // Mobius: quest items are not dropable
        if (item.Count < count)
            return "bad_request";

        bool useCoords = x != 0 || y != 0 || z != 0;
        int dropX = useCoords ? x : player.X;
        int dropY = useCoords ? y : player.Y;
        int dropZ = useCoords ? z : player.Z;
        AdvanceMove(player, _clock().ToUnixTimeMilliseconds());
        if (useCoords && !ZoneCombat.InDropRange(player.X, player.Y, player.Z, dropX, dropY, dropZ))
        {
            // Mobius: CANNOT_DISCARD_DISTANCE_TOO_FAR, item stays. Re-send the row so a client that
            // hid the slot on drag puts it back without a relog.
            ShotNotice(session, SysMsgDiscardTooFar, "That is too far from you to discard.");
            var keep = InventoryMessages.Update(InventoryChange.Modify, item);
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], keep);
            ZoneLog.Info($"drop-bag refused far object={session.ObjectId} item={item.ItemId} player={player.X},{player.Y},{player.Z} at={dropX},{dropY},{dropZ}");
            return "range";
        }

        int itemId = item.ItemId;
        if (!player.Inventory.TryReduce(item, count, out bool removedEntirely))
            return "bad_request";

        InventoryUpdateMsg invUpdate;
        if (removedEntirely)
        {
            invUpdate = new InventoryUpdateMsg { Changes = [InventoryMessages.RemoveRow(item, -1)] };
        }
        else
        {
            invUpdate = InventoryMessages.Update(InventoryChange.Modify, item);
        }

        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], invUpdate);

        SaveInventory(session, player.Inventory);

        var watchers = new List<ZoneEntity> { player };
        foreach (var other in Players())
        {
            if (ReferenceEquals(other, player))
                continue;
            if (other.Knownlist.Contains(player.ObjectId) || VisibleTo(other.ObjectId, player.ObjectId))
                watchers.Add(other);
        }

        SpawnGroundStack(
            itemId,
            count,
            dropX,
            dropY,
            dropZ,
            player.InstanceId,
            watchers,
            dropperObjectId: player.ObjectId);
        ZoneLog.Info($"drop-bag object={session.ObjectId} item={itemId} count={count} @ {dropX},{dropY},{dropZ}");
        return null;
    }

    /// <summary>Death loot onto the corpse tile — SpawnItem / DropItem fan-out for pickup + FX.</summary>
    void SpawnDeathDrops(
        int npcId,
        int x,
        int y,
        int z,
        ZoneEntity? killer,
        IReadOnlyList<ZoneEntity> watchers)
    {
        if (!_dropCatalog.TryGet(npcId, out var definition))
        {
            ZoneLog.Info($"drop miss npc={npcId} (no droplist)");
            return;
        }

        var rolls = DropRoller.RollDeath(definition, _dropRandom, _dropRates);
        if (rolls.Count == 0)
            ZoneLog.Info($"drop empty npc={npcId}");

        int instanceId = killer?.InstanceId
            ?? (watchers.Count > 0 ? watchers[0].InstanceId : 0);
        int dropperId = killer?.ObjectId ?? 0;
        int ownerId = killer?.Session != null ? killer.ObjectId : 0;
        long now = _clock().ToUnixTimeMilliseconds();

        foreach (var stack in rolls)
        {
            int dx = _dropRandom.Next(DeathDropScatter * 2 + 1) - DeathDropScatter;
            int dy = _dropRandom.Next(DeathDropScatter * 2 + 1) - DeathDropScatter;
            SpawnGroundStack(stack.ItemId, stack.Count, x + dx, y + dy, z, instanceId, watchers, dropperId,
                ownerObjectId: ownerId,
                expireAtMs: now + (IsHerb(stack.ItemId) ? HerbAutoDestroyMs : GroundItemAutoDestroyMs));
        }
    }

    void SpawnGroundStack(
        int itemId,
        int count,
        int x,
        int y,
        int z,
        int instanceId,
        IReadOnlyList<ZoneEntity> watchers,
        int dropperObjectId = 0,
        int ownerObjectId = 0,
        long expireAtMs = 0)
    {
        if (count <= 0)
            return;
        if (!_game.Items.TryGet(itemId, out var template))
        {
            ZoneLog.Info($"drop skip unknown item={itemId}");
            return;
        }

        int objectId = _itemIds.Next();
        var ground = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.GroundItem,
            InstanceId = instanceId,
            X = x,
            Y = y,
            Z = z,
            GroundItemId = itemId,
            GroundCount = count,
            GroundStackable = template.Stackable,
            GroundOwnerId = ownerObjectId,
            GroundProtectUntilMs = ownerObjectId != 0 ? _clock().ToUnixTimeMilliseconds() + DropProtectMs : 0,
            GroundExpireAtMs = expireAtMs
        };
        _byId[objectId] = ground;
        if (expireAtMs > 0)
            _expiringGround.Add(objectId);
        _aoi.Enter(objectId, x, y);

        // DropItem carries the dropper so the client plays throw/land FX.
        // AOI enter for late observers still uses SpawnItem via AddPresence.
        List<int>? netWatchers = null;
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            watcher.Knownlist.Add(objectId);
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDropItem(netWatchers, new DropItemMsg
            {
                CharObjectId = dropperObjectId,
                ObjectId = objectId,
                ItemId = itemId,
                X = x,
                Y = y,
                Z = z,
                Stackable = template.Stackable,
                Count = count
            });
        }

        ZoneLog.Info($"drop object={objectId} item={itemId} count={count} dropper={dropperObjectId} @ {x},{y},{z}");
    }

    void RemoveGroundItem(ZoneEntity ground)
    {
        int objectId = ground.ObjectId;
        _expiringGround.Remove(objectId);
        _aoi.Leave(objectId);
        _byId.Remove(objectId);

        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, objectId);
        }
    }

}
