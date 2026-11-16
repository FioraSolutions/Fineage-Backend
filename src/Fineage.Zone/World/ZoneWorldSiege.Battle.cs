using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Siege;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

/// <summary>
/// The running siege after Mobius <c>Siege.startSiege / endSiege / midVictory</c>, <c>ScheduleEndSiegeTask</c>
/// (the countdown), <c>Castle.updateClansReputation</c> and <c>Siege.teleportPlayer</c>. The board
/// (<see cref="SiegeBoard"/>) owns registration and ownership; this partial owns everything spawned for the fight.
/// </summary>
public sealed partial class ZoneWorld
{
    sealed class SiegeRuntime
    {
        public required int CastleId { get; init; }
        /// <summary>Owner when the siege started (Mobius <c>_firstOwnerClanId</c>).</summary>
        public int FirstOwnerClanId { get; init; }
        /// <summary>First clan that lost the castle during this fight (Mobius <c>_formerOwner</c>).</summary>
        public int FormerOwnerClanId { get; set; }
        /// <summary>Every clan that took part, so the end can still reach them after the board clears its rows.</summary>
        public HashSet<int> Clans { get; } = [];
        /// <summary>Control tower placement index → live object id.</summary>
        public Dictionary<int, int> ControlTowers { get; } = [];
        public List<int> FlameTowers { get; } = [];
        /// <summary>Guard object id → the control tower placement it is registered to (Mobius closest CT).</summary>
        public Dictionary<int, int> Guards { get; } = [];
        public List<(long DueMs, CastlePlacement Row, int Tower)> GuardRespawns { get; } = [];
        /// <summary>Attacker clan id → headquarters object id (Mobius <c>SiegeClan.getFlag</c>, MaxFlags = 1).</summary>
        public Dictionary<int, int> Flags { get; } = [];
        public long NextGuardScanMs { get; set; }
        public int NextCountdownMark { get; set; }
    }

    /// <summary>Mobius <c>SiegeGuards.AggroCheckTask</c> period.</summary>
    const int SiegeGuardScanMs = 3000;

    /// <summary>Mobius <c>ScheduleEndSiegeTask</c> steps: 1 h, 10 min, 5 min, 10 s before the end.</summary>
    static readonly long[] SiegeCountdownMarks = [3_600_000, 600_000, 300_000, 10_000];

    readonly Dictionary<int, SiegeRuntime> _siegeRun = [];

    internal CastleData CastleTable { get; set; } = CastleData.Default;

    /// <summary>Simulation tick: auto start at the siege time, the countdown, guard aggro and respawns, auto end.</summary>
    void TickSieges(long now)
    {
        foreach (var castle in _siege.Castles)
        {
            if (!castle.InProgress && castle.ScheduleAccepted && castle.SiegeUnixMs > 0 && castle.SiegeUnixMs <= now &&
                !_siegeRun.ContainsKey(castle.Id))
            {
                StartSiegeLocked(castle.Id, now);
                continue;
            }

            if (castle.InProgress && !_siegeRun.ContainsKey(castle.Id))
                OnSiegeStarted(castle, now, resume: true);
            if (!_siegeRun.TryGetValue(castle.Id, out var run))
                continue;
            if (castle.InProgress && castle.EndsUnixMs > 0 && castle.EndsUnixMs <= now)
            {
                if (_siege.EndSiege(castle.Id, now) == null)
                    SyncSiegeRuntime(now);
                continue;
            }

            AnnounceCountdown(castle, run, now);
            TickSiegeGuards(castle, run, now);
        }
    }

    SiegeStartKind StartSiegeLocked(int castleId, long now)
    {
        var before = _siege.Find(castleId);
        int owner = before?.OwnerClanId ?? 0;
        var kind = _siege.StartSiege(castleId, now);
        if (kind == SiegeStartKind.Cancelled && before != null)
        {
            // Mobius startSiege: no attackers → 846 (NPC castle) or 295 (owned), then the next date is picked.
            Broadcast(owner > 0 ? SiegeLines.CancelledOwned(before.Name) : SiegeLines.SiegeCancelled(before.Name));
            PersistSiege();
        }
        else if (kind == SiegeStartKind.Started)
        {
            SyncSiegeRuntime(now);
            PersistSiege();
        }

        return kind;
    }

    /// <summary>Starts or tears down the fight state for every castle whose board state moved.</summary>
    void SyncSiegeRuntime(long now)
    {
        foreach (var castle in _siege.Castles)
        {
            bool running = _siegeRun.ContainsKey(castle.Id);
            if (castle.InProgress && !running)
                OnSiegeStarted(castle, now, resume: false);
            else if (!castle.InProgress && running)
                OnSiegeEnded(castle, now);
        }
    }

    void OnSiegeStarted(CastleSiege castle, long now, bool resume)
    {
        var run = new SiegeRuntime { CastleId = castle.Id, FirstOwnerClanId = castle.OwnerClanId };
        _siegeRun[castle.Id] = run;
        NoteSiegeClans(castle, run);
        run.NextCountdownMark = FirstCountdownMark(castle.EndsUnixMs - now);
        if (!resume)
        {
            // Mobius startSiege: everybody but the owner clan leaves the siege field before the doors respawn.
            TeleportFromSiegeZone(castle.Id, keep: e => SiegeClanOf(e) != 0 && SiegeClanOf(e) == castle.OwnerClanId);
            Broadcast(SiegeLines.SiegeStarted(castle.Name));
            AnnounceTimeLeft(castle, castle.EndsUnixMs - now);
        }

        var data = CastleTable.Find(castle.Id);
        RespawnCastleDoors(castle.Id, weak: false);
        if (data != null)
        {
            SpawnSiegeTowers(run, data);
            // Mobius SiegeGuardManager.spawnSiegeGuard: the NPC garrison only stands for an NPC-owned castle.
            if (castle.OwnerClanId == 0)
                SpawnSiegeGuards(run, data, now);
        }

        RefreshSiegeRelations(run);
        ZoneLog.Info($"[siege] start castle={castle.Id} owner={castle.OwnerClanId} resume={resume} clans={run.Clans.Count}");
    }

    void OnSiegeEnded(CastleSiege castle, long now)
    {
        if (!_siegeRun.Remove(castle.Id, out var run))
            return;
        Broadcast(SiegeLines.SiegeFinished(castle.Name));
        if (castle.OwnerClanId > 0 && _clans.TryGetValue(castle.OwnerClanId, out var winner))
            Broadcast(SiegeLines.Victorious(winner.Name, castle.Name));
        else
            Broadcast(SiegeLines.Draw(castle.Name));

        UpdateSiegeReputation(run, castle.OwnerClanId);
        foreach (int flag in run.Flags.Values)
            DespawnSiegeNpc(flag);
        run.Flags.Clear();
        TeleportFromSiegeZone(castle.Id, keep: e => SiegeClanOf(e) != 0 && SiegeClanOf(e) == castle.OwnerClanId);
        foreach (int id in run.ControlTowers.Values.Concat(run.FlameTowers).Concat(run.Guards.Keys).ToArray())
            DespawnSiegeNpc(id);
        run.ControlTowers.Clear();
        run.FlameTowers.Clear();
        run.Guards.Clear();
        run.GuardRespawns.Clear();
        RespawnCastleDoors(castle.Id, weak: false);
        RefreshSiegeRelations(run);
        ZoneLog.Info($"[siege] end castle={castle.Id} owner={castle.OwnerClanId} first={run.FirstOwnerClanId}");
    }

    /// <summary>
    /// Mobius <c>Castle.setOwner</c> during a siege → <c>Siege.midVictory</c>: the winner hears Siege_Victory, attackers
    /// go back to their headquarters (or town), the new defenders lose their flags, gates come back at half HP and the
    /// towers respawn.
    /// </summary>
    void OnSiegeMidVictory(CastleSiege castle, int previousOwner, long now)
    {
        if (!_siegeRun.TryGetValue(castle.Id, out var run))
            return;
        if (run.FormerOwnerClanId == 0 && previousOwner > 0 && previousOwner != castle.OwnerClanId)
            run.FormerOwnerClanId = previousOwner;
        NoteSiegeClans(castle, run);
        if (previousOwner > 0 && previousOwner != castle.OwnerClanId && _clans.TryGetValue(previousOwner, out var loser))
            RemoveCirclets(loser, castle.Id);

        foreach (int defender in _siege.SideClans(castle.Id, attackers: false))
        {
            if (run.Flags.Remove(defender, out int flag))
                DespawnSiegeNpc(flag);
        }

        foreach (var player in Players())
        {
            if (player.Session == null || CastleAtEntity(player) != castle.Id)
                continue;
            int side = SiegeSide(SiegeClanOf(player), castle.Id);
            if (side == SiegeProtocol.StateDefender)
                continue;
            if (side == SiegeProtocol.StateAttacker && run.Flags.TryGetValue(SiegeClanOf(player), out int flagId) &&
                _byId.TryGetValue(flagId, out var hq))
            {
                Relocate(player, hq.X, hq.Y, hq.Z);
                continue;
            }

            var (x, y, z) = TownDestination(player);
            Relocate(player, x, y, z);
        }

        foreach (int id in run.ControlTowers.Values.Concat(run.FlameTowers).ToArray())
            DespawnSiegeNpc(id);
        run.ControlTowers.Clear();
        run.FlameTowers.Clear();
        if (CastleTable.Find(castle.Id) is { } data)
            SpawnSiegeTowers(run, data);
        RespawnCastleDoors(castle.Id, weak: true);

        if (_clans.TryGetValue(castle.OwnerClanId, out var owner))
            PlaySoundToClan(owner, "Siege_Victory");
        RefreshSiegeRelations(run);
    }

    void NoteSiegeClans(CastleSiege castle, SiegeRuntime run)
    {
        if (castle.OwnerClanId > 0)
            run.Clans.Add(castle.OwnerClanId);
        foreach (int id in _siege.SideClans(castle.Id, attackers: true))
            run.Clans.Add(id);
        foreach (int id in _siege.SideClans(castle.Id, attackers: false))
            run.Clans.Add(id);
    }

    /// <summary>Mobius <c>Castle.updateClansReputation</c> with this siege's former owner.</summary>
    void UpdateSiegeReputation(SiegeRuntime run, int ownerId)
    {
        _clans.TryGetValue(ownerId, out var owner);
        if (run.FormerOwnerClanId > 0 && _clans.TryGetValue(run.FormerOwnerClanId, out var former))
        {
            if (former != owner)
            {
                int reward = Math.Min(SiegeProtocol.TakeCastlePoints, Math.Max(0, former.D.Reputation));
                ChangeReputation(former, -SiegeProtocol.LoseCastlePoints);
                ClanLine(OnlineIds(former), SiegeProtocol.ReputationLost,
                    $"Since your clan was defeated in a siege, {SiegeProtocol.LoseCastlePoints} points have been deducted from your clan's reputation score and given to the opposing clan.",
                    SiegeProtocol.LoseCastlePoints.ToString());
                if (owner != null)
                    GainSiegeReputation(owner, reward);
            }
            else
                GainSiegeReputation(former, SiegeProtocol.CastleDefendedPoints);
        }
        else if (owner != null)
            GainSiegeReputation(owner, SiegeProtocol.TakeCastlePoints);
    }

    void GainSiegeReputation(ZoneClan clan, int points)
    {
        ChangeReputation(clan, points);
        ClanLine(OnlineIds(clan), SiegeProtocol.ReputationGained,
            $"Since your clan emerged victorious from the siege, {points} points have been added to your clan's reputation score.",
            points.ToString());
    }

    static int FirstCountdownMark(long left)
    {
        int i = 0;
        while (i < SiegeCountdownMarks.Length && SiegeCountdownMarks[i] >= left)
            i++;
        return i;
    }

    void AnnounceCountdown(CastleSiege castle, SiegeRuntime run, long now)
    {
        long left = castle.EndsUnixMs - now;
        long? crossed = null;
        while (run.NextCountdownMark < SiegeCountdownMarks.Length && left <= SiegeCountdownMarks[run.NextCountdownMark])
            crossed = SiegeCountdownMarks[run.NextCountdownMark++];
        if (crossed is { } mark)
            AnnounceTimeLeft(castle, mark);
    }

    /// <summary>Mobius <c>ScheduleEndSiegeTask</c> line to both sides: hours above one hour, then minutes, then seconds.</summary>
    void AnnounceTimeLeft(CastleSiege castle, long left)
    {
        SystemMessageMsg line;
        if (left > 3_600_000)
        {
            long hours = left / 3_600_000;
            line = new SystemMessageMsg { Id = SiegeProtocol.HoursLeft, Params = [hours.ToString()], Text = $"{hours} hour(s) until castle siege conclusion." };
        }
        else if (left >= 60_000)
        {
            long minutes = left / 60_000;
            line = new SystemMessageMsg { Id = SiegeProtocol.MinutesLeft, Params = [minutes.ToString()], Text = $"{minutes} minute(s) until castle siege conclusion." };
        }
        else
        {
            long seconds = Math.Max(0, left / 1000);
            line = new SystemMessageMsg { Id = SiegeProtocol.SecondsLeft, Params = [seconds.ToString()], Text = $"This castle siege will end in {seconds} second(s)!" };
        }

        var ids = SiegeParticipants(castle.Id);
        if (ids.Count > 0)
            NetBridge?.FanoutSystemMessage(ids, line);
    }

    /// <summary>Online members of the owner, defender and attacker clans (Mobius <c>announceToPlayer(…, true)</c>).</summary>
    List<int> SiegeParticipants(int castleId, bool attackers = true, bool defenders = true)
    {
        var clans = new HashSet<int>();
        if (attackers)
            clans.UnionWith(_siege.SideClans(castleId, attackers: true));
        if (defenders)
            clans.UnionWith(_siege.SideClans(castleId, attackers: false));
        var ids = new List<int>();
        foreach (int clanId in clans)
        {
            if (_clans.TryGetValue(clanId, out var clan))
                ids.AddRange(OnlineIds(clan));
        }

        ids.Sort();
        return ids;
    }

    int CastleAtEntity(ZoneEntity e) => SiegeAreas.CastleAt(e.X, e.Y, e.Z);

    /// <summary>Mobius <c>Siege.teleportPlayer(NotOwner / Spectator, TOWN)</c> over the castle's siege zone.</summary>
    void TeleportFromSiegeZone(int castleId, Func<ZoneEntity, bool> keep)
    {
        foreach (var player in Players())
        {
            if (player.Session == null || player.Dead || CastleAtEntity(player) != castleId || keep(player))
                continue;
            var (x, y, z) = TownDestination(player);
            Relocate(player, x, y, z);
        }
    }

    /// <summary>Mobius <c>Player.getSiegeState</c>: 1 attacker / 2 defender of any running siege, else 0.</summary>
    int PlayerSiegeState(ZoneEntity player)
    {
        if (player.Kind != ZoneEntityKind.Player || SiegeClanOf(player) == 0 || _siegeRun.Count == 0)
            return 0;
        foreach (int castleId in _siegeRun.Keys)
        {
            int side = SiegeSide(SiegeClanOf(player), castleId);
            if (side != 0)
                return side;
        }

        return 0;
    }

    /// <summary>Mobius <c>updatePlayerSiegeStateFlags</c>: every participant repaints its relation to its watchers.</summary>
    void RefreshSiegeRelations(SiegeRuntime run)
    {
        foreach (int clanId in run.Clans)
        {
            if (!_clans.TryGetValue(clanId, out var clan))
                continue;
            foreach (var session in OnlineMembers(clan))
            {
                if (_byId.TryGetValue(session.ObjectId, out var entity))
                    BroadcastRelation(entity);
            }
        }
    }

    void PlaySoundToClan(ZoneClan clan, string sound)
    {
        var ids = OnlineIds(clan);
        if (ids.Length > 0)
            NetBridge?.FanoutPlaySound(ids, 1, sound, 0, 0, 0, 0, 0);
    }

    /// <summary>Spawns a template NPC that belongs to the siege (tower, guard, headquarters).</summary>
    ZoneEntity? SpawnSiegeNpc(int npcId, int x, int y, int z, int heading, SiegeNpcInfo info, string? title = null)
    {
        if (!Fineage.Zone.Npcs.NpcCatalog.LoadDefault().ById.TryGetValue(npcId, out var template))
            return null;
        int objectId = AllocateQuestNpcObjectId();
        var def = SpawnCatalog.FromTemplate(objectId, template, x, y, z, heading, attackable: true);
        MaterializeSpawn(def, _activeMapId);
        if (!_byId.TryGetValue(objectId, out var npc))
            return null;
        npc.SiegeNpc = info;
        npc.MerchantCastleId = 0;
        if (title != null)
            npc.Title = title;
        if (info.Role != SiegeNpcRole.Guard)
        {
            // Towers and headquarters never move or think (Mobius Tower / SiegeFlag are not Attackable AI).
            npc.HomeSet = false;
        }

        FanoutNewNpc(npc);
        return npc;
    }

    void DespawnSiegeNpc(int objectId)
    {
        if (_byId.TryGetValue(objectId, out var npc) && npc.SiegeNpc != null && npc.SiegeNpc.Role != SiegeNpcRole.Door)
            DespawnNpc(npc);
    }

    void SpawnSiegeTowers(SiegeRuntime run, CastleDataRow data)
    {
        for (int i = 0; i < data.ControlTowers.Count; i++)
        {
            var row = data.ControlTowers[i];
            var npc = SpawnSiegeNpc(row.NpcId, row.X, row.Y, row.Z, row.Heading,
                new SiegeNpcInfo { Role = SiegeNpcRole.ControlTower, CastleId = data.Id });
            if (npc != null)
                run.ControlTowers[i] = npc.ObjectId;
        }

        foreach (var row in data.FlameTowers)
        {
            var npc = SpawnSiegeNpc(row.NpcId, row.X, row.Y, row.Z, row.Heading,
                new SiegeNpcInfo { Role = SiegeNpcRole.FlameTower, CastleId = data.Id });
            if (npc != null)
                run.FlameTowers.Add(npc.ObjectId);
        }
    }

    void SpawnSiegeGuards(SiegeRuntime run, CastleDataRow data, long now)
    {
        foreach (var row in data.Guards)
            SpawnSiegeGuard(run, data, row, ClosestTower(data, row));
    }

    void SpawnSiegeGuard(SiegeRuntime run, CastleDataRow data, CastlePlacement row, int tower)
    {
        var npc = SpawnSiegeNpc(row.NpcId, row.X, row.Y, row.Z, row.Heading,
            new SiegeNpcInfo { Role = SiegeNpcRole.Guard, CastleId = data.Id });
        if (npc != null)
            run.Guards[npc.ObjectId] = tower;
    }

    /// <summary>Mobius <c>Siege.getClosestCt</c>: a guard registers to the nearest control tower.</summary>
    static int ClosestTower(CastleDataRow data, CastlePlacement row)
    {
        int best = -1;
        long bestSq = long.MaxValue;
        for (int i = 0; i < data.ControlTowers.Count; i++)
        {
            var ct = data.ControlTowers[i];
            long dx = ct.X - (long)row.X, dy = ct.Y - (long)row.Y, dz = ct.Z - (long)row.Z;
            long sq = dx * dx + dy * dy + dz * dz;
            if (sq < bestSq)
            {
                bestSq = sq;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Mobius <c>SiegeGuards.AggroCheckTask</c> (every 3 s): an idle castle guard hates (999) the first visible
    /// playable in its aggro range that is not a defender of this castle. Dead guards come back after their
    /// respawn delay while their control tower stands (Mobius <c>ControlTower.doDie → stopRespawn</c>).
    /// </summary>
    void TickSiegeGuards(CastleSiege castle, SiegeRuntime run, long now)
    {
        if (run.GuardRespawns.Count > 0 && CastleTable.Find(castle.Id) is { } data)
        {
            foreach (var due in run.GuardRespawns.Where(r => r.DueMs <= now).ToArray())
            {
                run.GuardRespawns.Remove(due);
                if (due.Tower < 0 || run.ControlTowers.ContainsKey(due.Tower))
                    SpawnSiegeGuard(run, data, due.Row, due.Tower);
            }
        }

        if (now < run.NextGuardScanMs || run.Guards.Count == 0)
            return;
        run.NextGuardScanMs = now + SiegeGuardScanMs;
        var players = Players();
        foreach (int id in run.Guards.Keys)
        {
            if (!_byId.TryGetValue(id, out var guard) || guard.CurHp <= 0 || guard.Dead || guard.AggroTargetId > 0)
                continue;
            int range = guard.AggroRange > 0 ? guard.AggroRange : 1000;
            foreach (var p in players)
            {
                if (p.Dead || p.CurHp <= 0 || p.Session == null || !SameInstance(guard.ObjectId, p.ObjectId))
                    continue;
                if (SiegeSide(SiegeClanOf(p), castle.Id) == SiegeProtocol.StateDefender)
                    continue;
                long dx = guard.X - (long)p.X, dy = guard.Y - (long)p.Y;
                if (dx * dx + dy * dy > (long)range * range || !CanSeeTarget(guard, p))
                    continue;
                TrackThink(guard);
                AddHate(guard, p, 999);
                break;
            }
        }
    }

    /// <summary>A siege NPC died through the normal NPC death path (tower, headquarters, guard).</summary>
    void OnSiegeNpcDied(ZoneEntity npc)
    {
        var info = npc.SiegeNpc;
        if (info == null || !_siegeRun.TryGetValue(info.CastleId, out var run))
            return;
        switch (info.Role)
        {
            case SiegeNpcRole.ControlTower:
                foreach (var (index, id) in run.ControlTowers.ToArray())
                {
                    if (id == npc.ObjectId)
                        run.ControlTowers.Remove(index);
                }
                break;
            case SiegeNpcRole.FlameTower:
                run.FlameTowers.Remove(npc.ObjectId);
                break;
            case SiegeNpcRole.Flag:
                if (run.Flags.TryGetValue(info.FlagClanId, out int flag) && flag == npc.ObjectId)
                    run.Flags.Remove(info.FlagClanId);
                break;
            case SiegeNpcRole.Guard:
                if (run.Guards.Remove(npc.ObjectId, out int tower) && CastleTable.Find(info.CastleId) is { } data)
                {
                    var row = data.Guards.FirstOrDefault(g => g.NpcId == npc.NpcId && g.X == npc.HomeX && g.Y == npc.HomeY);
                    if (row != null && (tower < 0 || run.ControlTowers.ContainsKey(tower)))
                        run.GuardRespawns.Add((_clock().ToUnixTimeMilliseconds() + Math.Max(1, row.RespawnSeconds) * 1000L, row, tower));
                }
                break;
        }
    }

    /// <summary>Test hook: one simulation pass of the siege timers.</summary>
    internal void TickSiegesForTests()
    {
        lock (_gate)
            TickSieges(_clock().ToUnixTimeMilliseconds());
    }

    internal IReadOnlyList<ZoneEntity> SiegeNpcsForTests(int castleId)
    {
        lock (_gate)
            return _byId.Values.Where(e => e.SiegeNpc != null && e.SiegeNpc.CastleId == castleId).ToList();
    }
}
