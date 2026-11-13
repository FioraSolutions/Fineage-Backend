using Fineage.Protocol;
using Fineage.Zone.Combat;

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

    /// <summary>Mobius attack-timeout <c>teleToLocation(spawn)</c>: drop the fight and snap back to the spawn.</summary>
    void TeleportHome(ZoneEntity npc)
    {
        SetAggroTarget(npc, 0);
        npc.HateList.Clear();
        npc.DamageBy.Clear();
        npc.Chasing = false;
        npc.AttackTimeoutAtMs = 0;
        SetReturningHome(npc, false);
        StopTimedMove(npc);
        if (npc.Running)
        {
            npc.Running = false;
            FanoutMoveType(npc);
        }
        if (!npc.HomeSet)
            return;
        npc.X = npc.HomeX;
        npc.Y = npc.HomeY;
        npc.Z = npc.HomeZ;
        _aoi.Move(npc.ObjectId, npc.X, npc.Y);
        RefreshNpcWatchers(npc);
        BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z);
    }

    /// <summary>
    /// Mobius <c>AttackableThinkTaskManager</c> (<c>TASK_DELAY = 1000</c>) calls <c>AttackableAI.thinkActive</c>.
    /// Past <c>MaxDriftRange</c> with no target: <c>setWalking</c> + <c>returnHome</c> (<c>AttackableAI.java:627</c>).
    /// Otherwise <c>Rnd.get(RANDOM_WALK_RATE) == 0</c> (rate 30) and <c>isRandomWalkingEnabled</c>
    /// (<c>NpcTemplate</c> default true, false for Guard): offset inside the drift box, geo
    /// <c>getValidLocation</c>, move only if that point is still within drift (<c>AttackableAI.java:689</c>).
    /// The roll does not change run/walk. Chase sets running; return home sets walking.
    /// </summary>
    const int RandomWalkRate = 30;
    long _nextWanderScanMs;

    void TickRandomWalk(long now)
    {
        if (now < _nextWanderScanMs)
            return;
        _nextWanderScanMs = now + 1000;
        int drift = ZoneNpcSettings.MaxDriftRange;
        long driftSq = (long)drift * drift;
        if (_thinking.Count == 0)
            return;
        foreach (int id in _thinking.ToArray())
        {
            if (!_byId.TryGetValue(id, out var npc))
            {
                _thinking.Remove(id);
                continue;
            }
            if (npc.Kind != ZoneEntityKind.Npc || npc.CurHp <= 0 || !npc.HomeSet)
            {
                _thinking.Remove(id);
                continue;
            }
            if (!ZoneCombat.IsCombatNpcType(npc.NpcType))
                continue;
            // One step of _globalAggro toward 0 per think (AttackableAI.thinkActive).
            if (npc.GlobalAggro != 0)
                npc.GlobalAggro += npc.GlobalAggro < 0 ? 1 : -1;
            if (npc.AggroTargetId > 0 || npc.Chasing || npc.ReturningHome)
                continue;
            if (npc.CastingUntilMs > now || IsImmobilized(npc) || IsFeared(npc))
                continue;

            long hdx = npc.X - (long)npc.HomeX;
            long hdy = npc.Y - (long)npc.HomeY;
            long homeDistSq = hdx * hdx + hdy * hdy;
            // calculateDistance2D > MAX_DRIFT_RANGE. Equal does not return home.
            if (homeDistSq > driftSq)
            {
                ReturnHome(npc, now);
                continue;
            }

            // NpcTemplate randomWalk defaults to false only for Guard. No XML overrides in CT0 stats.
            if (npc.NpcType == "Guard")
                continue;
            if (TryFollowLeader(npc, now))
                continue;
            if (RndBound(RandomWalkRate) != 0)
                continue;
            // Same 1/30 roll: idle BUFF before the step (AttackableAI.java:691).
            if (TryNpcSelfBuffSkill(npc, now))
                continue;

            int x1 = npc.HomeX;
            int y1 = npc.HomeY;
            int z1 = npc.HomeZ;
            // isInsideRadius2D is strict: distSq < radius^2.
            if (homeDistSq < driftSq)
            {
                int deltaX = RndBound(drift * 2);
                int deltaY = RndInclusive(deltaX, drift * 2);
                deltaY = (int)Math.Sqrt((double)deltaY * deltaY - (double)deltaX * deltaX);
                x1 = deltaX + npc.HomeX - drift;
                y1 = deltaY + npc.HomeY - drift;
                z1 = npc.Z;
            }

            int destX = x1;
            int destY = y1;
            int destZ = z1;
            if (_geo.HasGeo(npc.X, npc.Y))
                (destX, destY, destZ) = _geo.MoveCheck(npc.X, npc.Y, npc.Z, x1, y1, z1, out _);
            long fdx = destX - (long)npc.HomeX;
            long fdy = destY - (long)npc.HomeY;
            if (fdx * fdx + fdy * fdy > driftSq)
                continue;
            if (destX == npc.X && destY == npc.Y)
                continue;
            StartNpcMove(npc, destX, destY, destZ, now);
        }
    }

    /// <summary>
    /// Mobius minion follow (<c>AttackableAI.java:640</c>): match the leader's gait and step inside
    /// offset 200 (raid minion 500) when farther than that. Close minions may buff on the 1/30 roll
    /// and do not random-walk away from the leader.
    /// </summary>
    bool TryFollowLeader(ZoneEntity npc, long now)
    {
        if (npc.LeaderObjectId <= 0 || !_byId.TryGetValue(npc.LeaderObjectId, out var leader) || leader.CurHp <= 0)
            return false;
        bool running = leader.Running;
        if (npc.Running != running)
        {
            npc.Running = running;
            FanoutMoveType(npc);
        }

        int offset = npc.RaidMinion ? 500 : 200;
        const int minRadius = 30;
        long dx = npc.X - (long)leader.X;
        long dy = npc.Y - (long)leader.Y;
        if (dx * dx + dy * dy > (long)offset * offset)
        {
            int x1 = RndInclusive(minRadius * 2, offset * 2);
            int y1 = RndInclusive(x1, offset * 2);
            y1 = (int)Math.Sqrt((double)y1 * y1 - (double)x1 * x1);
            int lx = x1 > offset + minRadius ? leader.X + x1 - offset : leader.X - x1 + minRadius;
            int ly = y1 > offset + minRadius ? leader.Y + y1 - offset : leader.Y - y1 + minRadius;
            StartNpcMove(npc, lx, ly, leader.Z, now);
            return true;
        }

        if (RndBound(RandomWalkRate) == 0)
            TryNpcSelfBuffSkill(npc, now);
        return true;
    }

    /// <summary>
    /// Mobius <c>Spawn</c> spawns <c>Privates</c> with the master. They are created when the master
    /// enters the think set (the region just became active), not for every master on an empty world.
    /// </summary>
    void SpawnPrivates(ZoneEntity master)
    {
        if (master.PrivatesSpawned)
            return;
        master.PrivatesSpawned = true;
        if (!_minions.TryGet(master.NpcId, out var rows))
            return;
        var templates = Fineage.Zone.Npcs.NpcCatalog.LoadDefault();
        foreach (var row in rows)
        {
            if (!templates.ById.TryGetValue(row.NpcId, out var template))
                continue;
            for (int n = 0; n < row.Count; n++)
            {
                int objectId = AllocateQuestNpcObjectId();
                int x = master.X + RndBound(80) - 40;
                int y = master.Y + RndBound(80) - 40;
                var entity = new ZoneEntity
                {
                    ObjectId = objectId,
                    Kind = ZoneEntityKind.Npc,
                    NpcId = template.NpcId,
                    Name = template.Name,
                    Title = template.Title,
                    NpcType = template.Type,
                    Level = template.Level,
                    X = x,
                    Y = y,
                    Z = master.Z,
                    Attackable = template.Attackable,
                    MaxHp = template.Hp,
                    CurHp = template.Hp,
                    MaxMp = template.Mp,
                    CurMp = template.Mp,
                    PAtk = template.PAtk,
                    PDef = template.PDef,
                    PAtkSpd = template.AttackSpeed,
                    AttackRange = template.AttackRange,
                    Aggressive = template.Aggressive,
                    AggroRange = template.AggroRange,
                    ClanHelpRange = template.ClanHelpRange,
                    Clans = template.Clans,
                    RunSpeed = template.RunSpeed,
                    WalkSpeed = template.WalkSpeed,
                    Running = master.Running,
                    GlobalAggro = -10,
                    HomeSet = true,
                    HomeX = x,
                    HomeY = y,
                    HomeZ = master.Z,
                    LeaderObjectId = master.ObjectId,
                    RaidMinion = master.NpcType is "RaidBoss" or "GrandBoss"
                };
                _byId.Add(objectId, entity);
                _aoi.Enter(objectId, x, y);
                master.MinionIds.Add(objectId);
                _thinking.Add(objectId);
                FanoutNewNpc(entity);
                SpawnPrivates(entity);
            }
        }
    }

    /// <summary>Mobius <c>Rnd.get(bound)</c>: 0 inclusive, bound exclusive.</summary>
    int RndBound(int bound) => bound <= 0 ? 0 : _combatRandom.Next(bound);

    /// <summary>Mobius <c>Rnd.get(origin, bound)</c>: both ends inclusive.</summary>
    int RndInclusive(int origin, int bound) =>
        origin >= bound ? origin : _combatRandom.Next(origin, bound + 1);

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
                TrackThink(npc);
                NetBridge?.FanoutKnownlistDiff(player.ObjectId, [npc.ObjectId], []);
                ZoneLog.Info($"[aoi] npc enter npc={npc.ObjectId} watcher={player.ObjectId} at={npc.X},{npc.Y}");
            }
            else if (player.InstanceId != npc.InstanceId ||
                     (!_aoi.StillKnown(player.ObjectId, npc.ObjectId) && !CombatPinned(player, npc)))
            {
                player.Knownlist.Remove(npc.ObjectId);
                NetBridge?.FanoutKnownlistDiff(player.ObjectId, [], [npc.ObjectId]);
                ReleaseThink(npc);
                ZoneLog.Info($"[aoi] npc leave npc={npc.ObjectId} watcher={player.ObjectId} at={npc.X},{npc.Y}");
            }
        }
    }
}
