using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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
}
