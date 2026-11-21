using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>WalkingManager</c>: follow <see cref="WalkingRouteCatalog"/> instead of idle random walk.</summary>
public sealed partial class ZoneWorld
{
    readonly Lazy<WalkingRouteCatalog> _walkingRoutes = new(WalkingRouteCatalog.LoadDefault);

    internal WalkingRouteCatalog WalkingRoutes => _walkingRoutes.Value;

    void AttachWalkingRoute(ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Npc || entity.WalkingRouteName != null)
            return;
        var route = WalkingRoutes.Match(entity.NpcId, entity.X, entity.Y, entity.Z);
        if (route == null)
            return;
        entity.WalkingRouteName = route.Name;
        entity.WalkingPointIndex = 0;
        entity.WalkingForward = true;
        entity.WalkingWaitUntilMs = 0;
        entity.HomeSet = false;
        ZoneLog.Info($"[walking] npc={entity.ObjectId} npcId={entity.NpcId} route={route.Name}");
    }

    void TickWalkingRoutes(long now)
    {
        if (WalkingRoutes.Count == 0)
            return;
        foreach (int id in Active(ActiveSets.Thinking))
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.WalkingRouteName == null)
                continue;
            if (npc.CurHp <= 0 || npc.MoveActive)
                continue;
            if (npc.AggroTargetId > 0 || npc.Chasing)
                continue;
            if (npc.WalkingWaitUntilMs > now)
                continue;
            if (WalkingRoutes.GetByName(npc.WalkingRouteName) is not { } route)
            {
                npc.WalkingRouteName = null;
                continue;
            }

            if (npc.WalkingPointIndex < 0 || npc.WalkingPointIndex >= route.Points.Length)
                npc.WalkingPointIndex = 0;

            var point = route.Points[npc.WalkingPointIndex];
            if (point.Chat != null && NetBridge != null && KnownWatchers(npc.ObjectId) is { Count: > 0 } watchers)
            {
                NetBridge.FanoutCreatureSay(watchers, new CreatureSayMsg
                {
                    ObjectId = npc.ObjectId,
                    Name = npc.Name,
                    ChatType = ChatProtocol.All,
                    Text = point.Chat
                });
            }

            npc.Running = point.Run;
            if (point.DelaySec > 0)
                npc.WalkingWaitUntilMs = now + point.DelaySec * 1000L;

            if (npc.X == point.X && npc.Y == point.Y && Math.Abs(npc.Z - point.Z) < 128)
            {
                AdvanceWalkingIndex(npc, route);
                continue;
            }

            StartNpcMove(npc, point.X, point.Y, point.Z, now);
        }
    }

    void AdvanceWalkingIndex(ZoneEntity npc, WalkingRoute route)
    {
        int count = route.Points.Length;
        if (count == 0)
            return;
        int idx = npc.WalkingPointIndex;
        if (!route.Repeat && idx >= count - 1)
        {
            npc.WalkingRouteName = null;
            return;
        }

        switch (route.Style)
        {
            case WalkingRepeatStyle.Back:
                if (npc.WalkingForward)
                {
                    if (idx >= count - 1)
                        npc.WalkingForward = false;
                    else
                        npc.WalkingPointIndex = idx + 1;
                }
                else
                {
                    if (idx <= 0)
                        npc.WalkingForward = true;
                    else
                        npc.WalkingPointIndex = idx - 1;
                }
                break;
            case WalkingRepeatStyle.Conveyor:
                npc.WalkingPointIndex = (idx + 1) % count;
                if (npc.WalkingPointIndex == 0)
                {
                    var p = route.Points[0];
                    npc.X = p.X;
                    npc.Y = p.Y;
                    npc.Z = p.Z;
                    _aoi.Move(npc.ObjectId, npc.X, npc.Y);
                    BroadcastNpcTeleport(npc, p.X, p.Y, p.Z);
                }
                break;
            case WalkingRepeatStyle.Random:
                npc.WalkingPointIndex = RndBound(npc, count);
                break;
            default:
                npc.WalkingPointIndex = (idx + 1) % count;
                break;
        }
    }

    void BroadcastNpcTeleport(ZoneEntity npc, int x, int y, int z)
    {
        RefreshNpcWatchers(npc);
        NetBridge?.FanoutTeleportToLocation(KnownWatchers(npc.ObjectId), npc.ObjectId, x, y, z, npc.Heading);
    }
}