using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using MessagePack;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TrySelectTargetAsync(ZoneSession session, SelectTargetMsg select, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = TrySelectTarget(session, select.TargetObjectId, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryAttackAsync(ZoneSession session, AttackRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var outbound = new List<Pending>();
        string? error;
        long approachMs = 0;
        lock (_gate)
        {
            int previous = session.AutoAttackTargetId;
            long now0 = _clock().ToUnixTimeMilliseconds();
            int requested = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
            bool alreadySwinging = previous > 0 && previous == requested && session.ApproachTargetId == 0 &&
                session.NextAutoAttackUnixMs > now0;
            // Re-click on the target already being hit: Mobius keeps the running attack, no extra swing.
            error = alreadySwinging ? null : TryAttack(session, request.TargetObjectId, outbound, out approachMs);
            if (error == null && !alreadySwinging)
            {
                session.AutoAttackTargetId = session.SelectedTargetId;
                // Approaching: first swing on arrival (Mobius onEvtArrived -> doAttack); else next swing after Pspd.
                session.NextAutoAttackUnixMs = _clock().ToUnixTimeMilliseconds() +
                    (approachMs > 0 ? approachMs : PlayerAttackTimeMs(session.ObjectId));
            }
        }
        if (error != null)
            return error;

        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    string? TrySelectTarget(ZoneSession session, int targetObjectId, List<Pending> toSelf)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (!_byId.TryGetValue(targetObjectId, out var target))
            return "target";

        // Interlude: click own pawn → target self (AOI never lists the observer).
        if (targetObjectId == session.ObjectId)
        {
            if (player.Dead || player.CurHp <= 0)
                return "target";

            session.SelectedTargetId = targetObjectId;
            toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
            {
                ObjectId = targetObjectId,
                ColorHint = 0
            }));
            return null;
        }

        // Friendly NPCs (guides, merchants) are selectable for talk UI; Attackable is attack-only.
        if (target.Kind != ZoneEntityKind.Npc || target.CurHp <= 0)
            return "target";
        if (!VisibleTo(session.ObjectId, targetObjectId))
            return "target";

        session.SelectedTargetId = targetObjectId;
        toSelf.Add(Pack(session, MsgType.MyTargetSelected, new MyTargetSelectedMsg
        {
            ObjectId = targetObjectId,
            ColorHint = InterludeStatus.TargetColor(player.Level, target.Level)
        }));
        return null;
    }

    string? TryAttack(ZoneSession session, int requestedTargetId, List<Pending> outbound) =>
        TryAttack(session, requestedTargetId, outbound, out _);

    /// <summary>
    /// Interlude RequestAttack: in range -> swing; out of range -> server moveToPawn (run to the target,
    /// MoveToAck to self + watchers) and the swing fires from <see cref="TickAutoAttacks"/> on arrival.
    /// <paramref name="approachMs"/> is the travel time when an approach was started, else 0.
    /// </summary>
    string? TryAttack(ZoneSession session, int requestedTargetId, List<Pending> outbound, out long approachMs)
    {
        approachMs = 0;
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (player.CastingUntilMs > _clock().ToUnixTimeMilliseconds())
            return null; // Mobius: auto-attack waits while casting

        int targetId = requestedTargetId > 0 ? requestedTargetId : session.SelectedTargetId;
        if (targetId <= 0)
            return "attack";
        if (requestedTargetId > 0)
            session.SelectedTargetId = targetId;

        if (!_byId.TryGetValue(targetId, out var target))
            return "attack";
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable || target.CurHp <= 0)
            return "attack";
        if (!VisibleTo(player.ObjectId, targetId))
            return "attack";
        if (!ZoneCombat.Within(player.X, player.Y, target.X, target.Y, PlayerReach(player)))
        {
            approachMs = ApproachTarget(player, target, PlayerReach(player), outbound);
            return approachMs < 0 ? "attack" : null;
        }

        var ps = PlayerStats(player);
        // Mobius doAttack: a charged weapon fires its soulshot on this swing (hit or miss) and is uncharged.
        bool soulshot = player.SoulshotCharged;
        player.SoulshotCharged = false;
        var (damage, flags) = ZoneCombat.RollMelee(_combatRandom, ps.PAtk, NpcPDef(target),
            ps.Accuracy, NpcEvasion(target), ps.CritRate, ps.RandomDamage,
            soulshot, (int)ps.WeaponGrade, NpcShieldRate(target), target.ShieldDef, ps.Weapon == Fineage.Game.WeaponType.Bow);

        var attack = new AttackMsg
        {
            AttackerObjectId = player.ObjectId,
            TargetObjectId = targetId,
            Damage = damage,
            Flags = flags,
            AttackerX = player.X,
            AttackerY = player.Y,
            AttackerZ = player.Z,
            TargetX = target.X,
            TargetY = target.Y,
            TargetZ = target.Z
        };

        // Swing now; HP drops at hitTime (Mobius onHitTimer).
        FanoutAttack(player, target, attack, outbound);
        long now = _clock().ToUnixTimeMilliseconds();
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        _meleeHits.Add(new PendingMeleeHit(player.ObjectId, targetId, damage, now + PlayerAttackTimeMs(player.ObjectId) / 2));

        return null;
    }

    // Physical stats. Players: class template values (same as UserInfo); NPCs: Mobius npcs.json.
    // TODO(combat parity): STR/level/weapon/armor/buff stat pipeline.
    int PlayerPAtkSpd(ZoneEntity player) => PlayerStats(player).PAtkSpd;

    long PlayerAttackTimeMs(int objectId) =>
        _byId.TryGetValue(objectId, out var p) ? ZoneCombat.AttackTimeMs(PlayerPAtkSpd(p)) : ZoneCombat.AutoAttackIntervalMs;

    double PlayerPAtk(ZoneEntity player) => PlayerStats(player).PAtk;

    double PlayerPDef(ZoneEntity player) => PlayerStats(player).PDef;

    int PlayerReach(ZoneEntity player) => ZoneCombat.PlayerAttackReach(PlayerStats(player).AttackRange);

    double NpcPDef(ZoneEntity npc) =>
        Math.Max(1d, StatMods(npc).Apply(CombatStat.PDef, npc.PDef > 0 ? npc.PDef : Math.Max(1, npc.Level) * 40d));

    int NpcPAtkSpd(ZoneEntity npc) =>
        Math.Max(1, StatMods(npc).Apply(CombatStat.PAtkSpd, npc.PAtkSpd > 0 ? npc.PAtkSpd : ZoneCombat.DefaultNpcPAtkSpd));

    (int Damage, byte Flags) NpcMeleeRoll(ZoneEntity npc, ZoneEntity player)
    {
        if (npc.PAtk <= 0)
            return (ZoneCombat.MobMeleeDamage(npc.Level), 0);
        var ps = PlayerStats(player);
        return ZoneCombat.RollMelee(_combatRandom, StatMods(npc).Apply(CombatStat.PAtk, npc.PAtk), ps.PDef, NpcAccuracy(npc), ps.Evasion,
            NpcCritPermille(npc), npc.RandomDamage, false, 0, ps.ShieldRate, ps.ShieldDef, false);
    }

    Task? _combatLoop;

    async Task CombatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ZoneCombat.AutoAttackTickMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var outbound = new List<Pending>();
                lock (_gate)
                {
                    long now = _clock().ToUnixTimeMilliseconds();
                    TickPlayerMovement(now, outbound);
                    TickNpcMovement(now, outbound);
                    TickPendingHits(now, outbound);
                    TickMeleeHits(now, outbound);
                    TickAutoAttacks(outbound);
                    TickPendingCasts(now, outbound);
                    TickMobAttacks(now, outbound);
                    TickCorpseDecay(now, outbound);
                }
                if (outbound.Count > 0)
                    await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void TickAutoAttacks(List<Pending> outbound)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || session.AutoAttackTargetId <= 0)
                continue;
            int targetId = session.AutoAttackTargetId;
            if (player.Dead || !_byId.TryGetValue(targetId, out var target) || target.CurHp <= 0 || session.SelectedTargetId != targetId)
            {
                session.AutoAttackTargetId = 0;
                session.ClearApproach();
                continue;
            }
            if (!VisibleTo(player.ObjectId, targetId))
            {
                session.AutoAttackTargetId = 0;
                session.ClearApproach();
                continue;
            }

            // Mid-run and the pawn moved: re-send MoveToPawn toward its new spot (Mobius follow).
            if (session.ApproachTargetId == targetId && now < session.ApproachArriveUnixMs)
            {
                if (!ZoneCombat.Within(target.X, target.Y, session.ApproachTargetX, session.ApproachTargetY, ZoneCombat.ApproachRepathDistance))
                {
                    long re = ApproachTarget(player, target, PlayerReach(player), outbound);
                    if (re < 0)
                    {
                        session.AutoAttackTargetId = 0;
                        session.ClearApproach();
                        SendActionFailed(player, outbound);
                        continue;
                    }
                    session.NextAutoAttackUnixMs = now + re;
                }
                continue;
            }

            if (now < session.NextAutoAttackUnixMs)
                continue;
            if (!ZoneCombat.Within(player.X, player.Y, target.X, target.Y, PlayerReach(player)))
            {
                // Target moved away: Mobius re-issues moveToPawn and keeps the attack intention.
                long ms = ApproachTarget(player, target, PlayerReach(player), outbound);
                if (ms < 0)
                {
                    session.AutoAttackTargetId = 0;
                    session.ClearApproach();
                    SendActionFailed(player, outbound);
                    continue;
                }
                session.NextAutoAttackUnixMs = now + ms;
                continue;
            }
            session.ClearApproach();
            session.NextAutoAttackUnixMs = now + PlayerAttackTimeMs(player.ObjectId);
            if (TryAttack(session, targetId, outbound) != null)
                session.AutoAttackTargetId = 0;
        }
    }

    static StatusUpdateMsg StatusFor(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
        Values = [entity.CurHp, entity.MaxHp]
    };

    readonly List<PendingMeleeHit> _meleeHits = new();

    void FanoutAttack(ZoneEntity attacker, ZoneEntity target, AttackMsg attack, List<Pending> outbound)
    {
        byte[] attackBody = MessagePackSerializer.Serialize(attack);
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, attacker) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(attacker.ObjectId);
            if (!cares)
                continue;
            outbound.Add(new Pending(watcher.Session, MsgType.Attack, attackBody));
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutAttackState(netWatchers, new Fineage.Net.Contract.AttackStateMsg
            {
                AttackerValue = (ulong)(uint)attack.AttackerObjectId,
                AttackerGeneration = 1,
                TargetValue = (ulong)(uint)attack.TargetObjectId,
                TargetGeneration = 1,
                Damage = attack.Damage,
                Flags = attack.Flags,
                AttackerX = attack.AttackerX,
                AttackerY = attack.AttackerY,
                AttackerZ = attack.AttackerZ,
                TargetX = attack.TargetX,
                TargetY = attack.TargetY,
                TargetZ = attack.TargetZ
            });
        }
    }

    void TickMeleeHits(long now, List<Pending> outbound)
    {
        if (_meleeHits.Count == 0)
            return;
        var due = _meleeHits.Where(h => h.DueUnixMs <= now).ToList();
        if (due.Count == 0)
            return;
        _meleeHits.RemoveAll(h => h.DueUnixMs <= now);
        foreach (var hit in due)
            ResolveMeleeHit(hit, now, outbound);
    }

    void ResolveMeleeHit(PendingMeleeHit hit, long now, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(hit.AttackerId, out var attacker) || attacker.CurHp <= 0 || attacker.Dead)
            return;
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.CurHp <= 0 || target.Dead)
            return;
        // Mobius onHitTimer: target ran out of reach during the swing -> no damage.
        int reach = (attacker.Kind == ZoneEntityKind.Npc ? ZoneCombat.MobAttackReach(attacker.AttackRange) : PlayerReach(attacker)) + ZoneCombat.HitRangeTolerance;
        if (!ZoneCombat.Within(attacker.X, attacker.Y, target.X, target.Y, reach))
            return;

        if (hit.Damage <= 0)
        {
            // Miss (Attack carried HitFlagMiss): no HP change / StatusUpdate, but the fight and hate still start.
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.Kind == ZoneEntityKind.Npc)
                AddHate(target, attacker);
            return;
        }

        target.CurHp = Math.Max(0, target.CurHp - hit.Damage);

        if (target.Kind == ZoneEntityKind.Npc)
        {
            FanoutVitals(target, StatusFor(target), outbound);
            AddHate(target, attacker);
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.CurHp <= 0)
            {
                RemoveDeadNpc(target, outbound, killer: attacker);
                GrantKillReward(attacker, target, outbound);
            }
            return;
        }

        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        FanoutVitals(target, PlayerVitalsStatus(target), outbound);
        if (target.CurHp <= 0)
        {
            attacker.AggroTargetId = 0;
            if (target.Session != null)
                target.Session.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, attacker.ObjectId, outbound);
        }
    }

    void FanoutCombat(ZoneEntity attacker, ZoneEntity target, AttackMsg attack, StatusUpdateMsg status, List<Pending> outbound)
    {
        byte[] attackBody = MessagePackSerializer.Serialize(attack);
        byte[] statusBody = MessagePackSerializer.Serialize(status);

        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, attacker) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(attacker.ObjectId);
            if (!cares)
                continue;

            outbound.Add(new Pending(watcher.Session, MsgType.Attack, attackBody));
            outbound.Add(new Pending(watcher.Session, MsgType.StatusUpdate, statusBody));
        }
    }

    void RemoveDeadNpc(ZoneEntity npc, List<Pending> outbound, ZoneEntity? killer = null)
    {
        // Kill quest steps are not applied here. IQuestKillHook is the later combat entry.
        int objectId = npc.ObjectId;
        int npcId = npc.NpcId;
        int x = npc.X;
        int y = npc.Y;
        int z = npc.Z;

        var watchers = new List<ZoneEntity>();
        foreach (var player in Players())
        {
            if (player.Knownlist.Contains(objectId))
                watchers.Add(player);
        }

        // Mobius Creature.doDie -> broadcastPacket(new Die(this)): client plays the death animation.
        npc.AggroTargetId = 0;
        npc.Chasing = false;
        byte[] dieBody = MessagePackSerializer.Serialize(new DieMsg { ObjectId = objectId });
        List<int>? netWatchers = null;
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            outbound.Add(new Pending(watcher.Session, MsgType.Die, dieBody));
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
            if (watcher.Session.SelectedTargetId == objectId)
                watcher.Session.AutoAttackTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDie(netWatchers, new Fineage.Net.Contract.EntityDieMsg
            {
                EntityValue = (ulong)(uint)objectId,
                EntityGeneration = 1,
                KillerObjectId = killer?.ObjectId ?? 0
            });
        }

        npc.SweepItems.Clear();
        if (npc.SpoilerObjectId != 0 && _dropCatalog.TryGet(npcId, out var definition))
        {
            foreach (var stack in DropRoller.RollCorpse(definition, _dropRandom, _dropRates))
                npc.SweepItems.Add(stack);
        }

        if (npc.SweepItems.Count > 0)
        {
            SpawnDeathDrops(npcId, x, y, z, killer, watchers, outbound);
            npc.CorpseAwaitingSweep = true;
            ZoneLog.Info($"corpse object={objectId} spoiler={npc.SpoilerObjectId} sweep={npc.SweepItems.Count}");
            return;
        }

        SpawnDeathDrops(npcId, x, y, z, killer, watchers, outbound);
        // Corpse lies for DEFAULT_CORPSE_TIME, then DeleteObject (TickCorpseDecay).
        npc.DecayAtMs = _clock().ToUnixTimeMilliseconds() + ZoneCombat.CorpseDecayMs;
    }

    void DespawnNpc(ZoneEntity npc, List<Pending> outbound)
    {
        int objectId = npc.ObjectId;

        _aoi.Leave(objectId);
        _byId.Remove(objectId);
        npc.SpoilerObjectId = 0;
        npc.SweepItems.Clear();
        npc.CorpseAwaitingSweep = false;
        npc.DecayAtMs = 0;
        npc.Chasing = false;
        npc.AggroTargetId = 0;
        npc.LastAiTickMs = 0;
        ScheduleRespawn(objectId);

        byte[] deleteBody = MessagePackSerializer.Serialize(new DeleteObjectMsg { ObjectId = objectId });
        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
                outbound.Add(new Pending(player.Session, MsgType.DeleteObject, deleteBody));
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
            if (player.Session.SelectedTargetId == objectId)
                player.Session.SelectedTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, new Fineage.Net.Contract.EntityDespawnMsg
            {
                EntityValue = (ulong)(uint)objectId,
                EntityGeneration = 1
            });
        }
    }

    bool VisibleTo(int observerId, int otherId)
    {
        if (!SameInstance(observerId, otherId))
            return false;
        foreach (int id in _aoi.GetVisible(observerId))
        {
            if (id == otherId)
                return true;
        }

        return false;
    }
}
