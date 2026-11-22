using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void TickPlayerMovementInRegion(RegionId region, IReadOnlyList<int> objectIds, long now)
    {
        foreach (int id in objectIds)
        {
            if (!_byId.TryGetValue(id, out var e) || e.Kind != ZoneEntityKind.Player)
                continue;
            TickOnePlayerMovement(e, now);
        }
    }

    void TickOnePlayerMovement(ZoneEntity e, long now)
    {
        bool wasMoving = e.MoveActive;
        if (e.MoveActive)
            AdvanceMove(e, now);
        if (e.MoveSegmentDirty)
        {
            e.MoveSegmentDirty = false;
            if (e.MoveActive)
            {
                var ack = new Fineage.Protocol.MoveToMsg
                {
                    ObjectId = e.ObjectId,
                    DestX = e.MoveDestX,
                    DestY = e.MoveDestY,
                    DestZ = e.MoveDestZ,
                    OriginX = e.X,
                    OriginY = e.Y,
                    OriginZ = e.Z
                };
                if (e.Session != null && e.Intent.ApproachTargetId > 0)
                {
                    e.Intent.ApproachOriginX = e.X;
                    e.Intent.ApproachOriginY = e.Y;
                    e.Intent.ApproachDestX = e.MoveDestX;
                    e.Intent.ApproachDestY = e.MoveDestY;
                }

                AtBarrier(() => RefreshMovingKnownlist(e, ack));
                return;
            }
        }

        if (!wasMoving)
            return;
        if (ShouldRefreshKnownlistAfterMove(e))
            AtBarrier(() => RefreshMovingKnownlist(e, null));
    }

    void TickNpcMovementInRegion(RegionId region, IReadOnlyList<int> objectIds, long now)
    {
        foreach (int id in objectIds)
        {
            if (!_byId.TryGetValue(id, out var e) || e.Kind != ZoneEntityKind.Npc || !e.MoveActive)
                continue;
            TickOneNpcMovement(e, now);
        }
    }

    void TickOneNpcMovement(ZoneEntity e, long now)
    {
        if (e.CurHp <= 0)
        {
            SetMoveActive(e, false);
            e.MovePath.Clear();
            return;
        }

        if (e.Kind == ZoneEntityKind.Npc && !NpcRegionAwake(e))
        {
            SuspendIdleNpcMove(e);
            return;
        }

        if (e.PathfindPending)
            return;
        AdvanceMove(e, now);
        if (ShouldRefreshKnownlistAfterMove(e))
            AtBarrier(() => RefreshNpcWatchers(e));
        if (e.MoveSegmentDirty)
        {
            e.MoveSegmentDirty = false;
            if (e.MoveActive)
            {
                AtBarrier(() => BroadcastNpcMove(e, e.MoveDestX, e.MoveDestY, e.MoveDestZ));
                return;
            }
        }

        if (!e.MoveActive)
        {
            AtBarrier(() =>
            {
                BroadcastNpcMove(e, e.X, e.Y, e.Z);
                SetReturningHome(e, false);
            });
        }
    }

    sealed class PlayerMovementStage(ZoneWorld world) : IRegionStage
    {
        public string Name => "movement.player";

        public bool Parallel => true;

        public void Collect(TickContext tick, RegionWorkCollector work)
        {
            if (world._byId.CountOf(ActiveSets.Moving) == 0)
                return;
            world._byId.Collect(ActiveSets.Moving, work);
        }

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds) =>
            world.TickPlayerMovementInRegion(region, objectIds, tick.NowUnixMs);
    }

    sealed class NpcMovementStage(ZoneWorld world) : IRegionStage
    {
        public string Name => "movement.npc";

        public bool Parallel => true;

        public void Collect(TickContext tick, RegionWorkCollector work)
        {
            if (world._byId.CountOf(ActiveSets.Moving) == 0)
                return;
            world._byId.Collect(ActiveSets.Moving, work);
        }

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds) =>
            world.TickNpcMovementInRegion(region, objectIds, tick.NowUnixMs);
    }
}