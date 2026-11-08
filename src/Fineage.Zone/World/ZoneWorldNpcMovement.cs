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
    void StartNpcMove(ZoneEntity npc, int wantX, int wantY, int wantZ, long now)
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
        SetMoveActive(npc, npc.X != destX || npc.Y != destY);
        BroadcastNpcMove(npc, destX, destY, destZ);
    }

    /// <summary>
    /// Mobius <c>Attackable.returnHome</c> (<c>Attackable.java:1685</c>): <c>clearAggroList()</c> and walk back to the
    /// spawn point. It does <b>not</b> heal — only the distance leash reset does (see
    /// <see cref="TryLeashReset"/>); a mob that simply lost its target heals through the normal regen tick.
    /// </summary>
    void ReturnHome(ZoneEntity npc, long now)
    {
        SetAggroTarget(npc, 0);
        npc.HateList.Clear();
        // Mobius clearAggroList drops the whole AggroList, damage shares included: a mob that disengaged must not
        // pay the old attacker's damage to whoever kills it next.
        npc.DamageBy.Clear();
        npc.Chasing = false;
        if (!npc.HomeSet)
        {
            StopTimedMove(npc);
            BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z);
            return;
        }
        SetReturningHome(npc, true);
        if (npc.Running)
        {
            npc.Running = false;
            FanoutMoveType(npc);
        }
        ZoneLog.Info($"[npc] return home obj={npc.ObjectId} from={npc.X},{npc.Y} home={npc.HomeX},{npc.HomeY}");
        StartNpcMove(npc, npc.HomeX, npc.HomeY, npc.HomeZ, now);
    }

    /// <summary>
    /// Mobius <c>AttackableAI.thinkAttack</c> distance leash (<c>AttackableAI.java:740</c>): a monster dragged further
    /// than <c>AggroDistanceCheckRange</c> (2000, raids 4000) from its spawn drops the fight and walks home. With
    /// <c>AggroDistanceCheckRestoreLife</c> it also refills HP/MP (the only Mobius full heal for a monster).
    /// <para>
    /// The shipped CT0 Interlude <c>NPC.ini</c> has <c>AggroDistanceCheckEnabled = False</c>, so this tick does nothing
    /// unless the operator turns it on — plain <c>returnHome</c> never heals.
    /// </para>
    /// </summary>
    void TickLeash(long now)
    {
        if (!ZoneNpcSettings.AggroDistanceCheckEnabled)
            return;
        int range = ZoneNpcSettings.AggroDistanceCheckRange;
        int raidRange = ZoneNpcSettings.AggroDistanceCheckRaidRange;
        bool raids = ZoneNpcSettings.AggroDistanceCheckRaids;
        bool restoreLife = ZoneNpcSettings.AggroDistanceCheckRestoreLife;
        if (_aiBusy.Count == 0)
            return;
        foreach (int id in _aiBusy.ToArray())
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc ||
                npc.CurHp <= 0 || npc.AggroTargetId <= 0 || !npc.HomeSet)
                continue;
            TryLeashReset(npc, range, raidRange, raids, restoreLife);
        }
    }

    /// <summary>
    /// Leash test for one monster. Returns true when the mob was reset and sent home.
    /// </summary>
    bool TryLeashReset(ZoneEntity npc, int range, int raidRange, bool checkRaids, bool restoreLife)
    {
        // Mobius skips GrandBosses and walkers; instance gating matches AGGRO_DISTANCE_CHECK_INSTANCES = False.
        if (npc.NpcType == "GrandBoss" || npc.InstanceId != 0 || !npc.HomeSet || npc.AggroTargetId <= 0)
            return false;
        bool raid = npc.NpcType == "RaidBoss";
        if (raid && !checkRaids)
            return false;

        long dx = npc.X - (long)npc.HomeX, dy = npc.Y - (long)npc.HomeY;
        int leash = Math.Max(ZoneNpcSettings.MaxDriftRange, raid ? raidRange : range);
        if (dx * dx + dy * dy <= (long)leash * leash)
            return false;

        if (restoreLife)
        {
            npc.CurHp = npc.MaxHp;
            npc.CurMp = npc.MaxMp;
            npc.RegenCarryHp = 0;
            npc.RegenCarryMp = 0;
            FanoutVitals(npc, StatusFor(npc));
        }
        ZoneLog.Info($"[aggro] leash reset npc={npc.ObjectId} from={npc.X},{npc.Y} home={npc.HomeX},{npc.HomeY} restore={restoreLife}");
        ReturnHome(npc, _clock().ToUnixTimeMilliseconds());
        return true;
    }

    /// <summary>Test seam: run the leash check with explicit ini values and no outbound buffer.</summary>
    internal bool LeashResetForTest(ZoneEntity npc, int range, int raidRange, bool checkRaids, bool restoreLife) =>
        TryLeashReset(npc, range, raidRange, checkRaids, restoreLife);

    /// <summary>Advance every moving NPC at walk/run speed; announce new path legs and the final stop.</summary>
    void TickNpcMovement(long now)
    {
        if (_moving.Count == 0)
            return;
        foreach (int id in _moving.ToArray())
        {
            if (!_byId.TryGetValue(id, out var e) || e.Kind != ZoneEntityKind.Npc || !e.MoveActive)
                continue;
            if (e.CurHp <= 0)
            {
                SetMoveActive(e, false);
                e.MovePath.Clear();
                continue;
            }
            AdvanceMove(e, now);
            long rdx = e.X - e.AoiRefreshX, rdy = e.Y - e.AoiRefreshY;
            if (!e.MoveActive || rdx * rdx + rdy * rdy >= (long)AoiRefreshDistance * AoiRefreshDistance)
                RefreshNpcWatchers(e);
            if (e.MoveSegmentDirty)
            {
                e.MoveSegmentDirty = false;
                if (e.MoveActive)
                {
                    BroadcastNpcMove(e, e.MoveDestX, e.MoveDestY, e.MoveDestZ);
                    continue;
                }
            }
            if (!e.MoveActive)
            {
                // Arrived: stop broadcast at the exact server spot (Mobius onEvtArrived / StopMove).
                BroadcastNpcMove(e, e.X, e.Y, e.Z);
                SetReturningHome(e, false); // stays walking; the next chase fans ChangeMoveType(run)
            }
        }
    }

    /// <summary>
    /// Moving NPC knownlist revalidate (Mobius knownlist update while the creature walks/chases): players whose view now
    /// covers the NPC get NpcInfo / Net EntitySpawn (+ MoveState), players it left get DeleteObject / EntityDespawn.
    /// Same hysteresis as players: StillKnown (5x5 cells) or combat pin keeps an existing link.
    /// </summary>
    void RefreshNpcWatchers(ZoneEntity npc)
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
                NetBridge?.FanoutKnownlistDiff(player.ObjectId, [npc.ObjectId], []);
                ZoneLog.Info($"[aoi] npc enter npc={npc.ObjectId} watcher={player.ObjectId} at={npc.X},{npc.Y}");
            }
            else if (player.InstanceId != npc.InstanceId ||
                     (!_aoi.StillKnown(player.ObjectId, npc.ObjectId) && !CombatPinned(player, npc)))
            {
                player.Knownlist.Remove(npc.ObjectId);
                NetBridge?.FanoutKnownlistDiff(player.ObjectId, [], [npc.ObjectId]);
                ZoneLog.Info($"[aoi] npc leave npc={npc.ObjectId} watcher={player.ObjectId} at={npc.X},{npc.Y}");
            }
        }
    }
}
