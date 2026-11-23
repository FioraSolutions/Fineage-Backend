using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

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
        if (npc.PathfindPending)
        {
            npc.PathfindPending = false;
            DequeueNpcPathfind(npc.ObjectId);
        }

        int destX = wantX, destY = wantY, destZ = wantZ;
        if (_geo.HasGeo(npc.X, npc.Y))
        {
            npc.Z = _geo.GetHeight(npc.X, npc.Y, npc.Z);
            (destX, destY, destZ) = _geo.MoveCheck(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ, out bool blocked);
            if (blocked)
            {
                if (TryNpcPathCacheOnly(npc.X, npc.Y, wantX, wantY, out var cached) && cached.Count > 0)
                {
                    (destX, destY, destZ) = cached[0];
                    for (int i = 1; i < cached.Count; i++)
                        npc.MovePath.Enqueue(cached[i]);
                }
                else
                {
                    RequestNpcGeoPath(npc, wantX, wantY, wantZ);
                    return;
                }
            }
        }
        else
            destZ = SanitizeDestZ(npc.X, npc.Y, npc.Z, wantX, wantY, wantZ);

        CommitNpcMoveStart(npc, destX, destY, destZ, now);
    }

    void SuspendIdleNpcMove(ZoneEntity npc)
    {
        if (npc.PathfindPending)
        {
            npc.PathfindPending = false;
            DequeueNpcPathfind(npc.ObjectId);
        }

        if (!npc.MoveActive)
            return;
        SetMoveActive(npc, false);
        npc.MovePath.Clear();
        npc.MoveSegmentDirty = false;
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
        ZoneHotPathLogs.NpcReturnHome(npc.ObjectId, npc.X, npc.Y, npc.HomeX, npc.HomeY);
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
    internal const int RandomWalkRate = 30;

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
            int x1 = RndInclusive(npc, minRadius * 2, offset * 2);
            int y1 = RndInclusive(npc, x1, offset * 2);
            y1 = (int)Math.Sqrt((double)y1 * y1 - (double)x1 * x1);
            int lx = x1 > offset + minRadius ? leader.X + x1 - offset : leader.X - x1 + minRadius;
            int ly = y1 > offset + minRadius ? leader.Y + y1 - offset : leader.Y - y1 + minRadius;
            StartNpcMove(npc, lx, ly, leader.Z, now);
            return true;
        }

        if (RndBound(npc, RandomWalkRate) == 0)
        {
            if (!TryNpcSelfBuffSkill(npc, now))
                TryNpcLeaderBuffSkill(npc, now);
        }
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
        if (RegionBodyScope.Of(this) != null)
        {
            // Placing entities changes region ownership, which only the scheduler thread may do. A parallel
            // random-walk body used to throw here and abort the rest of its region's think batch every tick.
            if (_minions.TryGet(master.NpcId, out _))
            {
                lock (_privatesPendingLock)
                    _privatesPending.Add(master.ObjectId);
            }
            return;
        }

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
                int x = master.X + RndBound(master, 80) - 40;
                int y = master.Y + RndBound(master, 80) - 40;
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
                    AttackType = template.AttackType,
                    Rhand = template.Rhand,
                    Lhand = template.Lhand,
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
                    RaidMinion = master.NpcType is "RaidBoss" or "GrandBoss",
                    AiBuffSkills = _npcAiSupport.Get(template.NpcId)?.Buff
                };
                _byId.Add(objectId, entity);
                _aoi.Enter(objectId, x, y);
                master.MinionIds.Add(objectId);
                _byId.Join(entity, ActiveSets.Thinking);
                QueueThinkReschedule(objectId);
                FanoutNewNpc(entity);
                SpawnPrivates(entity);
            }
        }
    }

    /// <summary>Mobius <c>Rnd.get(bound)</c>: 0 inclusive, bound exclusive.</summary>
    int RndBound(ZoneEntity npc, int bound) => bound <= 0 ? 0 : Rng(_combatRandom, npc).Next(bound);

    /// <summary>Mobius <c>Rnd.get(origin, bound)</c>: both ends inclusive.</summary>
    int RndInclusive(ZoneEntity npc, int origin, int bound) =>
        origin >= bound ? origin : Rng(_combatRandom, npc).Next(origin, bound + 1);

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
        if (_byId.CountOf(ActiveSets.AiBusy) == 0)
            return;
        foreach (int id in Active(ActiveSets.AiBusy))
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
        int leash = Math.Max(_maxDriftRange, raid ? raidRange : range);
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
        ZoneHotPathLogs.AggroLeashReset(npc.ObjectId, npc.X, npc.Y, npc.HomeX, npc.HomeY, restoreLife);
        ReturnHome(npc, NowUnixMs());
        return true;
    }

    /// <summary>Test seam: run the leash check with explicit ini values and no outbound buffer.</summary>
    internal bool LeashResetForTest(ZoneEntity npc, int range, int raidRange, bool checkRaids, bool restoreLife) =>
        TryLeashReset(npc, range, raidRange, checkRaids, restoreLife);

    /// <summary>Advance every moving NPC at walk/run speed; announce new path legs and the final stop.</summary>
    void TickNpcMovement(long now)
    {
        if (_byId.CountOf(ActiveSets.Moving) == 0)
            return;
        foreach (int id in Active(ActiveSets.Moving))
        {
            if (!_byId.TryGetValue(id, out var e) || e.Kind != ZoneEntityKind.Npc || !e.MoveActive)
                continue;
            if (e.CurHp <= 0)
            {
                SetMoveActive(e, false);
                e.MovePath.Clear();
                continue;
            }
            if (e.Kind == ZoneEntityKind.Npc && !NpcRegionAwake(e))
            {
                SuspendIdleNpcMove(e);
                continue;
            }

            if (e.PathfindPending)
                continue;
            AdvanceMove(e, now);
            if (ShouldRefreshKnownlistAfterMove(e))
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

    [ThreadStatic] static List<int>? t_visibleScratch;

    /// <summary>
    /// Moving NPC knownlist revalidate (Mobius knownlist update while the creature walks/chases): players whose view now
    /// covers the NPC get NpcInfo / Net EntitySpawn (+ MoveState), players it left get DeleteObject / EntityDespawn.
    /// Same hysteresis as players: StillKnown (5x5 cells) or combat pin keeps an existing link.
    /// </summary>
    void RefreshNpcWatchers(ZoneEntity npc)
    {
        StampKnownlistRefreshAnchor(npc);
        foreach (int playerId in WatcherPlayerIds(npc.ObjectId).ToArray())
        {
            if (!_byId.TryGetValue(playerId, out var player) || player.Session == null)
                continue;
            if (player.InstanceId != npc.InstanceId ||
                (!_aoi.StillKnown(player.ObjectId, npc.ObjectId) && !CombatPinned(player, npc)))
            {
                player.Knownlist.Remove(npc.ObjectId);
                UntrackWatcher(player.ObjectId, npc.ObjectId);
                NetBridge?.FanoutKnownlistDiff(player.ObjectId, [], [npc.ObjectId]);
                ReleaseThink(npc);
            }
        }

        var visible = t_visibleScratch ??= [];
        visible.Clear();
        _aoi.CollectVisible(npc.ObjectId, visible);
        foreach (int id in visible)
        {
            if (id == npc.ObjectId)
                continue;
            if (!_byId.TryGetValue(id, out var player) || player.Kind != ZoneEntityKind.Player || player.Session == null)
                continue;
            if (player.Knownlist.Contains(npc.ObjectId))
                continue;
            if (!VisibleTo(player.ObjectId, npc.ObjectId))
                continue;
            player.Knownlist.Add(npc.ObjectId);
            TrackWatcher(player.ObjectId, npc.ObjectId);
            TrackThink(npc);
            NetBridge?.FanoutKnownlistDiff(player.ObjectId, [npc.ObjectId], []);
        }
    }
}
