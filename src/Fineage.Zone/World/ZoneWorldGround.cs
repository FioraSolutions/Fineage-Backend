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
        ZoneLog.Info($"pickup object={session.ObjectId} ground={groundObjectId} item={ground.GroundItemId} count={ground.GroundCount}");
        return null;
    }

    void SpawnDeathDrops(int npcId, int x, int y, int z, IReadOnlyList<ZoneEntity> watchers, List<Pending> outbound)
    {
        if (!_dropCatalog.TryGet(npcId, out var definition))
            return;

        var rolls = DropRoller.RollDeath(definition, _dropRandom, _dropRates);
        foreach (var stack in rolls)
            SpawnGroundStack(stack.ItemId, stack.Count, x, y, z, watchers, outbound);
    }

    void SpawnGroundStack(int itemId, int count, int x, int y, int z, IReadOnlyList<ZoneEntity> watchers, List<Pending> outbound)
    {
        if (count <= 0)
            return;
        if (!_game.Items.TryGet(itemId, out var template))
            return;

        int objectId = _itemIds.Next();
        var ground = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.GroundItem,
            X = x,
            Y = y,
            Z = z,
            GroundItemId = itemId,
            GroundCount = count,
            GroundStackable = template.Stackable
        };
        _byId[objectId] = ground;
        _aoi.Enter(objectId, x, y);

        byte[] body = MessagePackSerializer.Serialize(SpawnItem(ground));
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            if (!VisibleTo(watcher.ObjectId, objectId))
                continue;
            watcher.Knownlist.Add(objectId);
            outbound.Add(new Pending(watcher.Session, MsgType.SpawnItem, body));
        }

        ZoneLog.Info($"drop object={objectId} item={itemId} count={count} @ {x},{y},{z}");
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
