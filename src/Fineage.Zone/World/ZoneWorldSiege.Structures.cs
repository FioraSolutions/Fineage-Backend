using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

/// <summary>
/// Castle gates / walls, towers, headquarters and the two siege skills after Mobius <c>Door</c>, <c>ControlTower</c>,
/// <c>FlameTower</c>, <c>SiegeFlag</c>, <c>Defender</c>, <c>ConditionPlayerCanTakeCastle</c>,
/// <c>ConditionPlayerCanCreateBase</c>, effects <c>TakeCastle</c> / <c>HeadquarterCreate</c>, and
/// <c>RequestRestartPoint</c> (castle / siege HQ restart).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>The player's clan from the clan table (the entity's cached field can lag behind a reload).</summary>
    int SiegeClanOf(ZoneEntity entity) => entity.Kind == ZoneEntityKind.Player ? ClanIdFor(entity.ObjectId) : 0;

    /// <summary>Mobius Doors.xml id → live door entity object id.</summary>
    readonly Dictionary<int, int> _castleDoors = [];
    bool _ownDoorTable;

    /// <summary>Gates and walls of every castle stand all the time (Mobius <c>DoorData</c> spawns them at boot).</summary>
    void SpawnCastleDoors()
    {
        var bounds = ZoneMapFilter.BoundsList;
        foreach (var data in CastleTable.All)
        {
            var castle = _siege.Board.Find(data.Id);
            foreach (var door in data.Doors)
            {
                // The gate stands where the geo door is (Doors.xml); a world without the door table has no gates.
                var geo = Doors.Get(door.Id);
                if (geo == null)
                    continue;
                int x = geo.X, y = geo.Y, z = geo.Z;
                if (!ZoneMapFilter.Contains(bounds, x, y))
                    continue;
                int ratio = castle != null && castle.DoorUpgrades.TryGetValue(door.Id, out int r) ? r : 1;
                int objectId = AllocateQuestNpcObjectId();
                var entity = new ZoneEntity
                {
                    ObjectId = objectId,
                    Kind = ZoneEntityKind.Npc,
                    NpcId = 0,
                    Name = door.Name,
                    NpcType = "Door",
                    X = x,
                    Y = y,
                    Z = z,
                    Level = 1,
                    Attackable = true,
                    MaxHp = door.Hp * ratio,
                    CurHp = door.Hp * ratio,
                    PDef = door.PDef,
                    MDef = door.MDef,
                    SiegeNpc = new SiegeNpcInfo
                    {
                        Role = SiegeNpcRole.Door,
                        CastleId = data.Id,
                        DoorId = door.Id,
                        Wall = door.Wall,
                        BaseHp = door.Hp
                    }
                };
                _byId.Add(objectId, entity);
                _aoi.Enter(objectId, x, y);
                _castleDoors[door.Id] = objectId;
            }
        }
    }

    /// <summary>
    /// Mobius <c>Castle.spawnDoor(isDoorWeak)</c>: every gate / wall back alive and closed, at full (or half) HP with the
    /// chamberlain upgrade applied.
    /// </summary>
    void RespawnCastleDoors(int castleId, bool weak)
    {
        var castle = _siege.Board.Find(castleId);
        foreach (var (doorId, objectId) in _castleDoors)
        {
            if (!_byId.TryGetValue(objectId, out var door) || door.SiegeNpc is not { } info || info.CastleId != castleId)
                continue;
            int ratio = castle != null && castle.DoorUpgrades.TryGetValue(doorId, out int r) ? r : 1;
            door.MaxHp = info.BaseHp * ratio;
            door.CurHp = weak ? (int)(door.MaxHp * SiegeProtocol.WeakDoorHpRatio) : door.MaxHp;
            door.Dead = false;
            SetGeoDoorOpen(doorId, false);
            FanoutDoorStatus(door);
        }
    }

    /// <summary>The datapack door table is shared by every world in the process; copy it before a door state changes.</summary>
    void SetGeoDoorOpen(int doorId, bool open)
    {
        var geo = Doors.Get(doorId);
        if (geo == null || geo.Open == open)
            return;
        if (!_ownDoorTable)
        {
            Doors = new DoorTable(Doors.All.Select(d => new DoorInstance
            {
                Id = d.Id,
                Name = d.Name,
                X = d.X,
                Y = d.Y,
                Z = d.Z,
                Nodes = d.Nodes,
                MinZ = d.MinZ,
                MaxZ = d.MaxZ,
                Open = d.Open,
                MinX = d.MinX,
                MaxX = d.MaxX,
                MinY = d.MinY,
                MaxY = d.MaxY
            }).ToList());
            _ownDoorTable = true;
            geo = Doors.Get(doorId)!;
        }

        geo.Open = open;
    }

    bool DoorOpen(ZoneEntity door) =>
        door.Dead || (door.SiegeNpc != null && Doors.Get(door.SiegeNpc.DoorId)?.Open == true);

    DoorStatusNetMsg DoorStatusOf(ZoneEntity door)
    {
        var info = door.SiegeNpc!;
        var castle = _siege.Board.Find(info.CastleId);
        return new DoorStatusNetMsg
        {
            ObjectId = door.ObjectId,
            DoorId = info.DoorId,
            Open = DoorOpen(door),
            CurHp = door.CurHp,
            MaxHp = door.MaxHp,
            Attackable = castle?.InProgress == true && !info.Wall,
            Wall = info.Wall,
            CastleId = info.CastleId,
            UpgradeRatio = castle != null && castle.DoorUpgrades.TryGetValue(info.DoorId, out int r) ? r : 1
        };
    }

    /// <summary>Follow-up frame for a door that just entered a client's view (see NetEnterWorldProjector).</summary>
    internal byte[]? DoorStatusFrame(ZoneEntity entity)
    {
        lock (_gate)
            return entity.SiegeNpc is { Role: SiegeNpcRole.Door } ? NetCodec.EncodeDoorStatus(DoorStatusOf(entity)) : null;
    }

    void FanoutDoorStatus(ZoneEntity door, ZoneEntity? also = null)
    {
        if (NetBridge == null)
            return;
        var watchers = NetWatchersOfDoor(door, also);

        if (watchers.Count > 0)
            NetBridge.FanoutDoorStatus(watchers, DoorStatusOf(door));
    }

    /// <summary>
    /// Damage gate for siege NPCs, run before the generic NPC damage. True when this call fully handled the hit
    /// (structure damage, or a refused hit); false lets the normal NPC path apply it (castle guards).
    /// </summary>
    bool SiegeNpcHit(ZoneEntity attacker, ZoneEntity target, int damage, long now)
    {
        var info = target.SiegeNpc;
        if (info == null)
            return false;
        var castle = _siege.Board.Find(info.CastleId);
        bool inProgress = castle?.InProgress == true;
        int side = attacker.Kind == ZoneEntityKind.Player ? SiegeSide(SiegeClanOf(attacker), info.CastleId) : 0;
        if (info.Role == SiegeNpcRole.Guard)
        {
            // Mobius SiegeGuards.onAttack: a defender of this castle never builds hate on its own garrison.
            if (side != SiegeProtocol.StateDefender)
                return false;
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            return true;
        }

        attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        if (attacker.Kind != ZoneEntityKind.Player || !inProgress || target.Dead || target.CurHp <= 0)
            return true;
        bool allowed = info.Role switch
        {
            // Mobius Door.isAutoAttackable (castle zone active, not the owner) and reduceCurrentHp: walls only take
            // siege-golem damage, so players never break them.
            SiegeNpcRole.Door => !info.Wall && (castle!.OwnerClanId == 0 || SiegeClanOf(attacker) != castle.OwnerClanId),
            // Mobius Tower.isAutoAttackable: only registered attackers.
            SiegeNpcRole.ControlTower or SiegeNpcRole.FlameTower => side == SiegeProtocol.StateAttacker,
            // Mobius SiegeFlag.isAutoAttackable: anyone but the owning clan's own members.
            SiegeNpcRole.Flag => SiegeClanOf(attacker) != info.FlagClanId,
            _ => false
        };
        if (!allowed || damage <= 0)
            return true;

        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, StatusFor(target));
        if (info.Role == SiegeNpcRole.Door)
        {
            FanoutDoorStatus(target, attacker);
            if (target.CurHp <= 0)
                BreakDoor(target, attacker, castle!);
            return true;
        }

        if (info.Role == SiegeNpcRole.Flag && now - info.LastAttackNoticeMs >= 30_000 && _clan.TryGet(info.FlagClanId, out var flagClan))
        {
            info.LastAttackNoticeMs = now;
            ClanLine(OnlineIds(flagClan), SiegeProtocol.BaseAttacked, "Your base is being attacked.");
        }

        if (target.CurHp <= 0)
            RemoveDeadNpc(target, killer: attacker);
        return true;
    }

    /// <summary>Mobius <c>Door.doDie</c>: the gate is open for good until the next respawn, 288 to everyone around.</summary>
    void BreakDoor(ZoneEntity door, ZoneEntity attacker, CastleSiege castle)
    {
        door.Dead = true;
        SetGeoDoorOpen(door.SiegeNpc!.DoorId, true);
        var watchers = NetWatchersOfDoor(door, attacker);

        if (watchers.Count > 0 && NetBridge != null)
        {
            NetBridge.FanoutDie(watchers, door.ObjectId, attacker.ObjectId);
            NetBridge.FanoutSystemMessage(watchers, new SystemMessageMsg
            {
                Id = SiegeProtocol.GateBroken,
                Params = [],
                Text = "The castle gate has been broken down."
            });
        }

        FanoutDoorStatus(door, attacker);
        ZoneLog.Info($"[siege] door {door.SiegeNpc.DoorId} castle={castle.Id} broken by {attacker.ObjectId}");
    }

    /// <summary>Seal of Ruler (246) and Build Headquarters (247), from <see cref="TrySkillUse"/>.</summary>
    string? TrySkillUseSiege(ZoneSession session, ZoneEntity player, SkillUseIntent request, SkillDefinition template,
        SkillLevelData levelData, int level, long now)
    {
        return request.SkillId == SiegeProtocol.SealOfRulerSkillId
            ? TrySealOfRuler(session, player, request, template, levelData, level, now)
            : TryBuildHeadquarters(session, player, template, levelData, level, now);
    }

    void UnsuitableTerms(ZoneSession session, SkillDefinition template) =>
        SelfLine(session, SiegeProtocol.UnsuitableTerms, $"{template.Name} cannot be used due to unsuitable terms.", template.Name);

    /// <summary>Mobius <c>ConditionPlayerCanTakeCastle</c>; null when the cast may go on.</summary>
    string? TakeCastleBlock(ZoneSession? session, ZoneEntity player, ZoneEntity? target, SkillDefinition template, out CastleSiege? castle)
    {
        castle = null;
        var clan = ClanOf(player.ObjectId);
        if (player.Dead || clan == null || clan.LeaderId != player.ObjectId)
            return SiegeProtocol.Leader;
        int castleId = CastleAtEntity(player);
        castle = castleId > 0 ? _siege.Board.Find(castleId) : null;
        if (castle == null || !castle.InProgress || SiegeSide(clan.Id, castle.Id) != SiegeProtocol.StateAttacker)
        {
            if (session != null)
                UnsuitableTerms(session, template);
            return SiegeProtocol.NotAttacker;
        }

        if (target == null || target.Kind != ZoneEntityKind.Npc || CastleTable.Find(castle.Id) is not { } data ||
            !data.ArtifactNpcIds.Contains(target.NpcId))
        {
            if (session != null)
                SelfLine(session, SiegeProtocol.InvalidTarget, "Invalid target.");
            return SiegeProtocol.NoTarget;
        }

        if (!ZoneCombat.Within(player.X, player.Y, target.X, target.Y, SiegeProtocol.EngraveRange) ||
            player.Z < target.Z || Math.Abs(player.Z - target.Z) > SiegeProtocol.EngraveMaxZDiff)
        {
            if (session != null)
                SelfLine(session, 748, "The distance is too far and so the casting has been stopped.");
            return SiegeProtocol.Range;
        }

        return null;
    }

    string? TrySealOfRuler(ZoneSession session, ZoneEntity player, SkillUseIntent request, SkillDefinition template,
        SkillLevelData levelData, int level, long now)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        _byId.TryGetValue(targetId, out var target);
        if (TakeCastleBlock(session, player, target, template, out var castle) is { } block)
            return block == SiegeProtocol.Leader ? SkillProtocol.Target : null;

        // Mobius ConditionPlayerCanTakeCastle: the defenders hear the engraving start.
        var defenders = SiegeParticipants(castle!.Id, attackers: false);
        if (defenders.Count > 0)
            NetBridge?.FanoutSystemMessage(defenders, new SystemMessageMsg
            {
                Id = SiegeProtocol.EngraveStarted,
                Params = [],
                Text = "The opposing clan has started to engrave the monument!"
            });

        StartStaticCast(session, player, target!, request.SkillId, template, levelData, level, now, (t, at) =>
        {
            // Mobius TakeCastle effect → Castle.engrave: the conditions hold at the end of the 3 minute cast too.
            if (TakeCastleBlock(null, player, t, template, out var still) != null || still == null)
                return;
            CaptureLocked(SiegeClanOf(player), still.Id, at);
        });
        return null;
    }

    /// <summary>Mobius <c>ConditionPlayerCanCreateBase</c>.</summary>
    string? CreateBaseBlock(ZoneSession? session, ZoneEntity player, SkillDefinition template, out CastleSiege? castle)
    {
        castle = null;
        var clan = ClanOf(player.ObjectId);
        if (player.Dead || clan == null)
            return SiegeProtocol.Clan;
        int castleId = CastleAtEntity(player);
        castle = castleId > 0 ? _siege.Board.Find(castleId) : null;
        bool ok = castle != null && castle.InProgress && SiegeSide(clan.Id, castle.Id) == SiegeProtocol.StateAttacker &&
                  clan.LeaderId == player.ObjectId &&
                  !(_siegeRun.TryGetValue(castle.Id, out var run) && run.Flags.ContainsKey(clan.Id));
        if (!ok)
        {
            if (session != null)
                UnsuitableTerms(session, template);
            return SiegeProtocol.Flag;
        }

        if (CastleTable.Find(castle!.Id) is not { } data || !data.InHqZone(player.X, player.Y, player.Z))
        {
            if (session != null)
                SelfLine(session, SiegeProtocol.BaseNotHere, "You cannot set up a base here.");
            return SiegeProtocol.Zone;
        }

        return null;
    }

    string? TryBuildHeadquarters(ZoneSession session, ZoneEntity player, SkillDefinition template, SkillLevelData levelData,
        int level, long now)
    {
        if (CreateBaseBlock(session, player, template, out _) != null)
            return null;
        if ((player.Inventory?.CountOf(SiegeProtocol.HeadquartersItemId) ?? 0) < SiegeProtocol.HeadquartersItemCount)
        {
            SelfLine(session, 351, "There are not enough necessary items to use the skill.");
            return null;
        }

        StartStaticCast(session, player, player, SiegeProtocol.BuildHeadquartersSkillId, template, levelData, level, now, (_, at) =>
        {
            if (CreateBaseBlock(session, player, template, out var castle) != null || castle == null)
                return;
            var inventory = player.Inventory;
            var changes = new List<InventoryUpdateEntryMsg>();
            if (inventory == null || !Take(inventory, SiegeProtocol.HeadquartersItemId, SiegeProtocol.HeadquartersItemCount, changes))
            {
                SelfLine(session, 351, "There are not enough necessary items to use the skill.");
                return;
            }

            FanInventory(session, Collapse(changes));
            SaveInventory(session, inventory);
            string title = _clan.TryGet(SiegeClanOf(player), out var clan) ? clan.Name : "";
            var flag = SpawnSiegeNpc(SiegeProtocol.HeadquartersNpcId, player.X, player.Y, player.Z, player.Heading,
                new SiegeNpcInfo { Role = SiegeNpcRole.Flag, CastleId = castle.Id, FlagClanId = SiegeClanOf(player) }, title);
            if (flag != null && _siegeRun.TryGetValue(castle.Id, out var run))
                run.Flags[SiegeClanOf(player)] = flag.ObjectId;
        });
        return null;
    }

    /// <summary>MP, reuse, MagicSkillUse, then the effect at hitTime. Static skills (isMagic 2) ignore cast speed.</summary>
    void StartStaticCast(ZoneSession session, ZoneEntity player, ZoneEntity target, int skillId, SkillDefinition template,
        SkillLevelData levelData, int level, long now, Action<ZoneEntity, long> effect)
    {
        ConsumeSkillMp(player, template, levelData.MpConsume);
        int hitMs = Math.Max(0, template.HitTimeMs);
        int reuseMs = Math.Max(0, template.ReuseDelayMs);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[skillId] = now + reuseMs;
        var cast = BuildCast(player, target, skillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + hitMs;
        if (!ReferenceEquals(player, target))
            session.SelectedTargetId = target.ObjectId;
        ScheduleEffect(player, target, template, level, now, hitMs, (t, at) => effect(t, at));
    }

    /// <summary>Mobius <c>Die</c> restart buttons for a dead player: bit0 castle, bit1 siege headquarters.</summary>
    int RestartFlagsFor(ZoneEntity player)
    {
        if (player.Kind != ZoneEntityKind.Player || SiegeClanOf(player) == 0)
            return 0;
        int flags = 0;
        if (CastleRestartPoint(player) != null)
            flags |= DeathProtocol.RestartCastle;
        if (SiegeHqRestartPoint(player) != null)
            flags |= DeathProtocol.RestartSiegeHq;
        return flags;
    }

    /// <summary>
    /// Mobius <c>RequestRestartPoint</c> case 2: a defender of a running siege restarts inside its castle; otherwise
    /// the owner clan restarts in its own castle. Attackers have no castle point.
    /// </summary>
    (int X, int Y, int Z)? CastleRestartPoint(ZoneEntity player)
    {
        int clanId = SiegeClanOf(player);
        if (clanId == 0)
            return null;
        int castleId = 0;
        foreach (int running in _siegeRun.Keys)
        {
            int side = SiegeSide(clanId, running);
            if (side == SiegeProtocol.StateDefender)
            {
                castleId = running;
                break;
            }

            if (side == SiegeProtocol.StateAttacker)
                return null;
        }

        if (castleId == 0)
            castleId = _siege.Board.CastleOwnedBy(clanId);
        if (castleId == 0 || CastleTable.Find(castleId) is not { OwnerRestart.Count: > 0 } data)
            return null;
        var p = data.OwnerRestart[RndBound(player, data.OwnerRestart.Count)];
        return (p.X, p.Y, p.Z);
    }

    /// <summary>Mobius <c>RequestRestartPoint</c> case 3: attacker clan with a standing headquarters.</summary>
    (int X, int Y, int Z)? SiegeHqRestartPoint(ZoneEntity player)
    {
        foreach (var run in _siegeRun.Values)
        {
            if (run.Flags.TryGetValue(SiegeClanOf(player), out int flagId) && _byId.TryGetValue(flagId, out var flag) && !flag.Dead &&
                flag.CurHp > 0)
                return (flag.X, flag.Y, flag.Z);
        }

        return null;
    }

    /// <summary>Test hook: place the holy artifact of <paramref name="castleId"/> next to the given point.</summary>
    internal ZoneEntity SpawnArtifactForTests(int castleId, int x, int y, int z)
    {
        lock (_gate)
        {
            int npcId = CastleTable.Find(castleId)!.ArtifactNpcIds[0];
            var template = Fineage.Zone.Npcs.NpcCatalog.LoadDefault().ById[npcId];
            int objectId = AllocateQuestNpcObjectId();
            MaterializeSpawn(Fineage.Zone.Spawns.SpawnCatalog.FromTemplate(objectId, template, x, y, z, 0), _activeMapId);
            var npc = _byId[objectId];
            npc.HomeSet = false;
            FanoutNewNpc(npc);
            return npc;
        }
    }

    /// <summary>Test hook: worlds with injected geo start without doors; give them a door table and spawn the gates.</summary>
    internal void LoadCastleDoorsForTests(DoorTable table)
    {
        lock (_gate)
        {
            Doors = table;
            _ownDoorTable = false;
            SpawnCastleDoors();
        }
    }

    /// <summary>Test hook: one landed hit of <paramref name="damage"/> through the siege structure gate.</summary>
    internal bool SiegeHitForTests(ZoneEntity attacker, ZoneEntity target, int damage)
    {
        lock (_gate)
            return SiegeNpcHit(attacker, target, damage, NowUnixMs());
    }

    internal ZoneEntity? CastleDoorForTests(int doorId)
    {
        lock (_gate)
            return _castleDoors.TryGetValue(doorId, out int id) && _byId.TryGetValue(id, out var door) ? door : null;
    }

    internal bool GeoDoorOpenForTests(int doorId)
    {
        lock (_gate)
            return Doors.Get(doorId)?.Open == true;
    }
}
