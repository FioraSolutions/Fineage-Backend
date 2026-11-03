using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// NPC/mob timed movement (Mobius <c>Creature.moveToLocation</c> for monsters): same speed-tick mover as players
/// (<see cref="AdvanceMove"/>), geo MoveCheck / A* path legs, MoveToAck + Net MoveState per leg and a final stop
/// broadcast on arrival. Used by chase (moveToPawn) and returnHome.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Start (or re-target) a timed NPC move to dest and broadcast the first leg to watchers.</summary>
    void StartNpcMove(ZoneEntity npc, int wantX, int wantY, int wantZ, long now, List<Pending> outbound)
    {
        AdvanceMove(npc, now);
        npc.MovePath.Clear();
        npc.MoveSegmentDirty = false;
        int destX = wantX, destY = wantY, destZ;
        if (_geo.HasGeo(npc.X, npc.Y))
        {
            npc.Z = _geo.GetHeight(npc.X, npc.Y, npc.Z);
            (destX, destY, destZ) = _geo.MoveCheck(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ, out bool blocked);
            if (blocked && _geo.FindPath(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ) is { Count: > 0 } path)
            {
                ZoneLog.Info($"[geo] npc path obj={npc.ObjectId} from={npc.X},{npc.Y},{npc.Z} want={wantX},{wantY} nodes={path.Count}");
                (destX, destY, destZ) = path[0];
                for (int i = 1; i < path.Count; i++)
                    npc.MovePath.Enqueue(path[i]);
            }
            else if (blocked)
                ZoneLog.Info($"[geo] npc move blocked obj={npc.ObjectId} want={wantX},{wantY} stop={destX},{destY},{destZ}");
        }
        else
        {
            destZ = SanitizeDestZ(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ);
        }

        if (ClipDoors(npc, ref destX, ref destY, ref destZ))
            npc.MovePath.Clear();

        npc.MoveDestX = destX;
        npc.MoveDestY = destY;
        npc.MoveDestZ = destZ;
        npc.MoveLastTickMs = now;
        npc.MoveActive = npc.X != destX || npc.Y != destY;
        BroadcastNpcMove(npc, destX, destY, destZ, outbound);
    }

    /// <summary>Mobius returnHome: forget target, walk back to the spawn point on the timed mover.</summary>
    void ReturnHome(ZoneEntity npc, long now, List<Pending> outbound)
    {
        npc.AggroTargetId = 0;
        npc.HateList.Clear();
        npc.Chasing = false;
        if (!npc.HomeSet)
        {
            StopTimedMove(npc);
            BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z, outbound);
            return;
        }
        npc.ReturningHome = true;
        if (npc.Running)
        {
            npc.Running = false;
            FanoutMoveType(npc, outbound);
        }
        ZoneLog.Info($"[npc] return home obj={npc.ObjectId} from={npc.X},{npc.Y} home={npc.HomeX},{npc.HomeY}");
        StartNpcMove(npc, npc.HomeX, npc.HomeY, npc.HomeZ, now, outbound);
    }

    /// <summary>Advance every moving NPC at walk/run speed; announce new path legs and the final stop.</summary>
    void TickNpcMovement(long now, List<Pending> outbound)
    {
        foreach (var e in _byId.Values)
        {
            if (e.Kind != ZoneEntityKind.Npc || !e.MoveActive)
                continue;
            if (e.CurHp <= 0)
            {
                e.MoveActive = false;
                e.MovePath.Clear();
                continue;
            }
            AdvanceMove(e, now);
            long rdx = e.X - e.AoiRefreshX, rdy = e.Y - e.AoiRefreshY;
            if (!e.MoveActive || rdx * rdx + rdy * rdy >= (long)AoiRefreshDistance * AoiRefreshDistance)
                RefreshNpcWatchers(e, outbound);
            if (e.MoveSegmentDirty)
            {
                e.MoveSegmentDirty = false;
                if (e.MoveActive)
                {
                    BroadcastNpcMove(e, e.MoveDestX, e.MoveDestY, e.MoveDestZ, outbound);
                    continue;
                }
            }
            if (!e.MoveActive)
            {
                // Arrived: stop broadcast at the exact server spot (Mobius onEvtArrived / StopMove).
                BroadcastNpcMove(e, e.X, e.Y, e.Z, outbound);
                e.ReturningHome = false; // stays walking; the next chase fans ChangeMoveType(run)
            }
        }
    }

    /// <summary>
    /// Moving NPC knownlist revalidate (Mobius knownlist update while the creature walks/chases): players whose view now
    /// covers the NPC get NpcInfo / Net EntitySpawn (+ MoveState), players it left get DeleteObject / EntityDespawn.
    /// Same hysteresis as players: StillKnown (5x5 cells) or combat pin keeps an existing link.
    /// </summary>
    void RefreshNpcWatchers(ZoneEntity npc, List<Pending> outbound)
    {
        npc.AoiRefreshX = npc.X;
        npc.AoiRefreshY = npc.Y;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            bool known = player.Knownlist.Contains(npc.ObjectId);
            if (!known)
            {
                if (!VisibleTo(player.ObjectId, npc.ObjectId))
                    continue;
                player.Knownlist.Add(npc.ObjectId);
                AddPresence(outbound, player.Session, npc);
                NetBridge?.FanoutKnownlistDiff(player.ObjectId, [npc.ObjectId], []);
                ZoneLog.Info($"[aoi] npc enter npc={npc.ObjectId} watcher={player.ObjectId} at={npc.X},{npc.Y}");
            }
            else if (player.InstanceId != npc.InstanceId ||
                     (!_aoi.StillKnown(player.ObjectId, npc.ObjectId) && !CombatPinned(player, npc)))
            {
                player.Knownlist.Remove(npc.ObjectId);
                outbound.Add(Pack(player.Session, MsgType.DeleteObject, new DeleteObjectMsg { ObjectId = npc.ObjectId }));
                NetBridge?.FanoutKnownlistDiff(player.ObjectId, [], [npc.ObjectId]);
                ZoneLog.Info($"[aoi] npc leave npc={npc.ObjectId} watcher={player.ObjectId} at={npc.X},{npc.Y}");
            }
        }
    }
}
