using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    int NpcPAtkSpd(ZoneEntity npc) =>
        Math.Max(1, StatMods(npc).Apply(CombatStat.PAtkSpd, npc.PAtkSpd > 0 ? npc.PAtkSpd : ZoneCombat.DefaultNpcPAtkSpd));

    (int Damage, byte Flags) NpcMeleeRoll(ZoneEntity npc, ZoneEntity player)
    {
        if (npc.PAtk <= 0)
            return (ZoneCombat.MobMeleeDamage(npc.Level), 0);
        var ps = PlayerStats(player);
        npc.Heading = ZoneCombat.HeadingFrom(npc.X, npc.Y, player.X, player.Y);
        // Mobius Npc.rechargeShots(physical): chance vs SoulShotRate, then stock--; charged for this swing.
        bool ss = TryNpcSoulshot(npc);
        return ZoneCombat.RollMelee(_combatRandom, StatMods(npc).Apply(CombatStat.PAtk, npc.PAtk), ps.PDef, NpcAccuracy(npc), ps.Evasion,
            NpcCritPermille(npc), npc.RandomDamage, ss, 0, ps.ShieldRate, ps.ShieldDef, false,
            ZoneCombat.HitConditionBonus(npc.X, npc.Y, npc.Z, player.X, player.Y, player.Z, player.Heading, IsNight),
            StatMods(npc).Apply(CombatStat.CritDmg, 1d));
    }

    /// <summary>Mobius <c>Npc.rechargeShots(true, false)</c>: fire one soulshot if stock and chance allow.</summary>
    bool TryNpcSoulshot(ZoneEntity npc)
    {
        if (npc.NpcSoulShotLeft <= 0 || npc.NpcSoulShotChance <= 0)
            return false;
        if (_combatRandom.Next(100) >= npc.NpcSoulShotChance)
            return false;
        npc.NpcSoulShotLeft--;
        return true;
    }

    Task? _combatLoop;

    /// <summary>One live combat-loop tick (movement, hits, player auto-attack / approach, casts, mobs, corpses, aggro/leash).</summary>
    internal async Task RunPlayerCombatTickAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            TickPlayerMovement(now);
            TickNpcMovement(now);
            TickPendingHits(now);
            TickMeleeHits(now);
            TickPvpFlags(now);
            TickAutoAttacks();
            TickPendingCasts(now);
            TickQueuedMoves(now);
            TickPendingPickups(now);
            // Proactive AI used to live only on RunCombatTickAsync (tests). Without these,
            // aggressive mobs never aggro or leash on the live CombatLoopAsync path.
            TickAggression(now);
            TickLeash(now);
            TickMobAttacks(now);
            TickCorpseDecay(now);
            TickAttackStance(now);
            TickGroundExpiry(now);
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    async Task CombatLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ZoneCombat.AutoAttackTickMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await RunPlayerCombatTickAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void TickAutoAttacks()
    {
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || session.AutoAttackTargetId <= 0)
                continue;
            AdvanceMove(player, now); // Net timed approach: measure reach from where the pawn really is now
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

            // Mid-run: repath if the target drifted, or if we stopped short of reach (geo wall / failed path).
            if (session.ApproachTargetId == targetId && now < session.ApproachArriveUnixMs)
            {
                bool targetMoved = !ZoneCombat.Within(target.X, target.Y, session.ApproachTargetX, session.ApproachTargetY,
                    ZoneCombat.ApproachRepathDistance);
                bool stuckShort = !player.MoveActive
                    && !ZoneCombat.Within(player.X, player.Y, target.X, target.Y, PlayerReach(player));
                if (targetMoved || stuckShort)
                {
                    long re = ApproachTarget(player, target, PlayerReach(player));
                    if (re < 0)
                    {
                        session.AutoAttackTargetId = 0;
                        session.ClearApproach();
                        SendActionFailed(player);
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
                long ms = ApproachTarget(player, target, PlayerReach(player));
                if (ms < 0)
                {
                    session.AutoAttackTargetId = 0;
                    session.ClearApproach();
                    SendActionFailed(player);
                    continue;
                }
                session.NextAutoAttackUnixMs = now + ms;
                continue;
            }
            session.ClearApproach();
            session.NextAutoAttackUnixMs = now + PlayerAttackTimeMs(player.ObjectId);
            if (TryAttack(session, targetId) != null)
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

    void QueueAttackerDamageLine(ZoneEntity attacker, ZoneEntity target, int damage)
    {
        // Mobius YOU_HIT_S1_FOR_S2_DAMAGE goes to the attacker only, never to watchers.
        if (damage <= 0 || attacker.Session == null || NetBridge == null)
            return;

        NetBridge.FanoutSystemMessage(
            [attacker.ObjectId],
            CombatLines.HitTarget(target.Name, damage));
    }

    void FanoutAttack(ZoneEntity attacker, ZoneEntity target, AttackMsg attack)
    {
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
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutAttackState(netWatchers, attack);
        }
    }

    void TickMeleeHits(long now)
    {
        if (_meleeHits.Count == 0)
            return;
        var due = _meleeHits.Where(h => h.DueUnixMs <= now).ToList();
        if (due.Count == 0)
            return;
        _meleeHits.RemoveAll(h => h.DueUnixMs <= now);
        foreach (var hit in due)
            ResolveMeleeHit(hit, now);
    }

    void ResolveMeleeHit(PendingMeleeHit hit, long now)
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
            FanoutVitals(target, StatusFor(target));
            RecordDamage(target, attacker, hit.Damage);
            AddHate(target, attacker, DamageHate(hit.Damage, target.Level));
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.CurHp <= 0)
            {
                RemoveDeadNpc(target, killer: attacker);
                GrantKillReward(attacker, target);
            }
            return;
        }

        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        FanoutVitals(target, PlayerVitalsStatus(target));
        if (attacker.Kind == ZoneEntityKind.Player && hit.Damage > 0)
            UpdatePvPStatus(attacker, target, now);
        if (target.CurHp > 0)
            MaybeBreakCast(target, hit.Damage);
        if (target.CurHp <= 0)
        {
            attacker.HateList.Remove(target.ObjectId);
            SetAggroTarget(attacker, MostHated(attacker));
            if (target.Session != null)
                target.Session.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, attacker.ObjectId);
        }
    }

    void RemoveDeadNpc(ZoneEntity npc, ZoneEntity? killer = null)
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
        SetAggroTarget(npc, 0);
        npc.Chasing = false;
        List<int>? netWatchers = null;
        foreach (var watcher in watchers)
        {
            if (watcher.Session == null)
                continue;
            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
            if (watcher.Session.SelectedTargetId == objectId)
                watcher.Session.AutoAttackTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutDie(netWatchers, objectId, killer?.ObjectId ?? 0);
        }

        npc.SweepItems.Clear();
        if (npc.SpoilerObjectId != 0 && _dropCatalog.TryGet(npcId, out var definition))
        {
            foreach (var stack in DropRoller.RollCorpse(definition, _dropRandom, _dropRates))
                npc.SweepItems.Add(stack);
        }

        SpawnDeathDrops(npcId, x, y, z, killer, watchers);
        bool spoiled = npc.SpoilerObjectId != 0;
        npc.CorpseAwaitingSweep = npc.SweepItems.Count > 0;
        if (spoiled)
            ZoneLog.Info($"corpse object={objectId} spoiler={npc.SpoilerObjectId} sweep={npc.SweepItems.Count}");
        // Mobius DecayTaskManager: DefaultCorpseTime, + SpoiledCorpseExtendTime when spoiled; unswept spoil decays with the corpse.
        SetDecayAtMs(npc, _clock().ToUnixTimeMilliseconds() + _spoilRules.CorpseMs(spoiled));
    }

    void DespawnNpc(ZoneEntity npc)
    {
        int objectId = npc.ObjectId;

        _aoi.Leave(objectId);
        _byId.Remove(objectId);
        npc.SpoilerObjectId = 0;
        npc.SweepItems.Clear();
        npc.CorpseAwaitingSweep = false;
        SetDecayAtMs(npc, 0);
        npc.Chasing = false;
        SetAggroTarget(npc, 0);
        npc.LastAiTickMs = 0;
        ForgetActiveSets(objectId);
        ScheduleRespawn(objectId);

        List<int>? netWatchers = null;
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            if (player.Knownlist.Remove(objectId))
            {
                netWatchers ??= [];
                netWatchers.Add(player.ObjectId);
            }
            if (player.Session.SelectedTargetId == objectId)
                player.Session.SelectedTargetId = 0;
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutEntityDespawn(netWatchers, objectId);
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
