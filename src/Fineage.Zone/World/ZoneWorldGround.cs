using Fineage.Protocol;
using Fineage.Zone.Combat;
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

    void SpawnGremlinDrop(int x, int y, int z, IReadOnlyList<ZoneEntity> watchers, List<Pending> outbound)
    {
        if (!_game.Items.TryGet(TalkingIsland.GremlinDropItemId, out var template))
            return;

        int objectId = _itemIds.Next();
        var ground = new ZoneEntity
        {
            ObjectId = objectId,
            Kind = ZoneEntityKind.GroundItem,
            X = x,
            Y = y,
            Z = z,
            GroundItemId = TalkingIsland.GremlinDropItemId,
            GroundCount = TalkingIsland.GremlinDropCount,
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

        ZoneLog.Info($"drop object={objectId} item={ground.GroundItemId} count={ground.GroundCount} @ {x},{y},{z}");
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
