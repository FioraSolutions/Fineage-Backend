using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryPickupAsync(ZoneSession session, PickupRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryPickup(session, request.ObjectId, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryDropItemAsync(ZoneSession session, DropItemRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
            error = TryDropItem(session, request, outbound);
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TryPickup(ZoneSession session, int groundObjectId, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (groundObjectId <= 0)
            return "bad_request";
        if (!_byId.TryGetValue(groundObjectId, out var ground) || ground.Kind != ZoneEntityKind.GroundItem)
            return "pickup";
        if (!VisibleTo(player.ObjectId, groundObjectId))
            return "pickup";
        if (!ZoneCombat.InMeleeRange(player.X, player.Y, ground.X, ground.Y))
            return "pickup";

        var added = player.Inventory.Add(ground.GroundItemId, ground.GroundCount);
        outbound.Add(Pack(session, MsgType.InventoryUpdate,
            InventoryMessages.Update(InventoryChange.Add, added)));
        RemoveGroundItem(ground, outbound);
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"pickup object={session.ObjectId} ground={groundObjectId} item={ground.GroundItemId} count={ground.GroundCount}");
        return null;
    }

    string? TryDropItem(ZoneSession session, DropItemRequestMsg request, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (request.ObjectId <= 0 || request.Count < 1)
            return "bad_request";

        var item = player.Inventory.Find(request.ObjectId);
        if (item == null || item.IsEquipped)
            return InventoryProtocol.NotOwned;
        if (item.Count < request.Count)
            return "bad_request";

        bool useCoords = request.X != 0 || request.Y != 0 || request.Z != 0;
        int dropX = useCoords ? request.X : player.X;
        int dropY = useCoords ? request.Y : player.Y;
        int dropZ = useCoords ? request.Z : player.Z;
        if (!ZoneCombat.InMeleeRange(player.X, player.Y, dropX, dropY))
            return "range";

        int itemId = item.ItemId;
        if (!player.Inventory.TryReduce(item, request.Count, out bool removedEntirely))
            return "bad_request";

        if (removedEntirely)
        {
            outbound.Add(Pack(session, MsgType.InventoryUpdate,
                new InventoryUpdateMsg { Changes = [InventoryMessages.RemoveRow(item, -1)] }));
        }
        else
        {
            outbound.Add(Pack(session, MsgType.InventoryUpdate,
                InventoryMessages.Update(InventoryChange.Modify, item)));
        }

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
            request.Count,
            dropX,
            dropY,
            dropZ,
            player.InstanceId,
            watchers,
            outbound,
            dropperObjectId: player.ObjectId);
        ZoneLog.Info($"drop-bag object={session.ObjectId} item={itemId} count={request.Count} @ {dropX},{dropY},{dropZ}");
        return null;
    }

    /// <summary>Death loot onto the corpse tile — SpawnItem / DropItem fan-out for pickup + FX.</summary>
    void SpawnDeathDrops(
        int npcId,
        int x,
        int y,
        int z,
        ZoneEntity? killer,
        IReadOnlyList<ZoneEntity> watchers,
        List<Pending> outbound)
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

        foreach (var stack in rolls)
            SpawnGroundStack(stack.ItemId, stack.Count, x, y, z, instanceId, watchers, outbound, dropperId);
    }

    void SpawnGroundStack(
        int itemId,
        int count,
        int x,
        int y,
        int z,
        int instanceId,
        IReadOnlyList<ZoneEntity> watchers,
        List<Pending> outbound,
        int dropperObjectId = 0)
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
            GroundStackable = template.Stackable
        };
        _byId[objectId] = ground;
        _aoi.Enter(objectId, x, y);

        // DropItem carries the dropper so the client plays throw/land FX.
        // AOI enter for late observers still uses SpawnItem via AddPresence.
        byte[] body = MessagePackSerializer.Serialize(new DropItemMsg
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
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            watcher.Knownlist.Add(objectId);
            outbound.Add(new Pending(watcher.Session, MsgType.DropItem, body));
        }

        ZoneLog.Info($"drop object={objectId} item={itemId} count={count} dropper={dropperObjectId} @ {x},{y},{z}");
    }

    void RemoveGroundItem(ZoneEntity ground, List<Pending> outbound)
    {
        int objectId = ground.ObjectId;
        _aoi.Leave(objectId);
        _byId.Remove(objectId);

        byte[] deleteBody = MessagePackSerializer.Serialize(new DeleteObjectMsg { ObjectId = objectId });
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
                outbound.Add(new Pending(player.Session, MsgType.DeleteObject, deleteBody));
        }
    }
}
