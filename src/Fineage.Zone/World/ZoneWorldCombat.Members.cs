using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Drops;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    int NpcPAtkSpd(ZoneEntity npc) =>
        Math.Max(1, StatMods(npc).Apply(CombatStat.PAtkSpd, npc.PAtkSpd > 0 ? npc.PAtkSpd : ZoneCombat.DefaultNpcPAtkSpd));

    (int Damage, byte Flags) NpcMeleeRoll(ZoneEntity npc, ZoneEntity player)
    {
        if (npc.PAtk <= 0)
            return (ZoneCombat.MobMeleeDamage(npc.Level), 0);
        double pDef, evasion, shieldRate;
        int shieldDef;
        if (player.Kind == ZoneEntityKind.Npc)
        {
            // NPC against NPC (summon ↔ monster): both sides from their templates.
            pDef = NpcPDef(player);
            evasion = NpcEvasion(player);
            shieldRate = ShieldRateInArc(player, npc, NpcShieldRate(player), 0);
            shieldDef = player.ShieldDef;
        }
        else
        {
            var ps = PlayerStats(player);
            pDef = ps.PDef;
            evasion = ps.Evasion;
            shieldRate = ShieldRateInArc(player, npc, ps.ShieldRate, ps.ShieldDefAngle);
            shieldDef = ps.ShieldDef;
        }
        npc.Heading = ZoneCombat.HeadingFrom(npc.X, npc.Y, player.X, player.Y);
        // Mobius Npc.rechargeShots(physical): chance vs SoulShotRate, then stock--; charged for this swing.
        bool ss = TryNpcSoulshot(npc);
        return ZoneCombat.RollMelee(Rng(_combatRandom, npc), StatMods(npc).Apply(CombatStat.PAtk, npc.PAtk), pDef, NpcAccuracy(npc), evasion,
            NpcCritPermille(npc), npc.RandomDamage, ss, 0, shieldRate, shieldDef, false,
            ZoneCombat.HitConditionBonus(npc.X, npc.Y, npc.Z, player.X, player.Y, player.Z, player.Heading, IsNight),
            StatMods(npc).Apply(CombatStat.CritDmg, 1d));
    }

    /// <summary>Mobius <c>Npc.rechargeShots(true, false)</c>: fire one soulshot if stock and chance allow.</summary>
    bool TryNpcSoulshot(ZoneEntity npc)
    {
        if (npc.NpcSoulShotLeft <= 0 || npc.NpcSoulShotChance <= 0)
            return false;
        if (Rng(_combatRandom, npc).Next(100) >= npc.NpcSoulShotChance)
            return false;
        npc.NpcSoulShotLeft--;
        FanoutNpcShotGlow(npc, soul: true);
        return true;
    }

    /// <summary>
    /// One combat cadence on demand (tests): the every-step stages the live loop runs, at the current tick, without
    /// advancing the clock, so slow stages keep their phase.
    /// </summary>
    internal async Task RunPlayerCombatTickAsync(CancellationToken ct)
    {
        RunStep(now => _simulationTicks.RunPeriod(1, now));
        _fanoutDispatcher.WaitUntilIdle();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Players swinging this tick, bucketed by owning region for the auto-attacks region stage.</summary>
    void CollectAutoAttackers(TickContext tick, RegionWorkCollector work)
    {
        foreach (var player in Players())
        {
            if (player.Session != null && player.Intent.AutoAttackTargetId > 0)
                work.Add(_byId.OwnerOf(player).Region, player.ObjectId);
        }
    }

    void AutoAttackEntity(int objectId, long now)
    {
        if (_byId.TryGetValue(objectId, out var player) && player.Kind == ZoneEntityKind.Player)
            TickAutoAttack(player, now);
    }

    void TickAutoAttack(ZoneEntity player, long now)
    {
        var session = player.Session;
        if (session == null || player.Intent.AutoAttackTargetId <= 0)
            return;
        AdvanceMoveSettled(player, now); // Net timed approach: measure reach from where the pawn really is now
        int targetId = player.Intent.AutoAttackTargetId;
        if (player.Dead || !_byId.TryGetValue(targetId, out var target) || target.CurHp <= 0 || session.SelectedTargetId != targetId)
        {
            player.Intent.AutoAttackTargetId = 0;
            player.Intent.ClearApproach();
            return;
        }
        if (!VisibleTo(player.ObjectId, targetId))
        {
            player.Intent.AutoAttackTargetId = 0;
            player.Intent.ClearApproach();
            return;
        }
        if (target.MoveActive)
            AdvanceMoveSettled(target, now);
        if (player.Sitting || IsActionBlocked(player) || IsPhysicallyMuted(player) ||
            HostilePlayableRefused(session, player, target, badSkill: false) ||
            SiegeFriendRefused(session, player, target) ||
            PeacePlayableRefused(session, player, target, notify: false))
        {
            player.Intent.AutoAttackTargetId = 0;
            player.Intent.ClearApproach();
            return;
        }

        // Mid-run: repath if the target drifted, or if we stopped short of reach (geo wall / failed path).
        if (player.Intent.ApproachTargetId == targetId && now < player.Intent.ApproachArriveUnixMs)
        {
            bool targetMoved = !ZoneCombat.Within(target.X, target.Y, player.Intent.ApproachTargetX, player.Intent.ApproachTargetY,
                ZoneCombat.ApproachRepathDistance);
            // A pathfind already queued for this approach is not "stuck". Re-entering here
            // would ask for another search on every tick. A pawn that failed to close the distance also waits out its
            // backoff first: the target must actually move, or the wait expire, before the search is paid for again.
            bool stuckShort = !player.MoveActive && !player.PathfindPending
                && !ZoneCombat.Within(player.X, player.Y, target.X, target.Y, PlayerReach(player, target));
            if ((targetMoved || stuckShort) && ApproachRetryDue(player, target, now))
            {
                long re = ApproachTarget(player, target, PlayerReach(player, target));
                if (re < 0)
                {
                    player.Intent.AutoAttackTargetId = 0;
                    player.Intent.ClearApproach();
                    SendActionFailed(player);
                    return;
                }
                player.Intent.NextAutoAttackUnixMs = now + re;
            }
            return;
        }

        if (now < player.Intent.NextAutoAttackUnixMs)
            return;
        if (!ZoneCombat.Within(player.X, player.Y, target.X, target.Y, PlayerReach(player, target)))
        {
            if (!ApproachRetryDue(player, target, now))
            {
                // Still backing off from a failed attempt at this target; re-check once the wait is over instead of
                // re-running the search every tick.
                player.Intent.NextAutoAttackUnixMs = now + ApproachRetryDelayMs(player.Intent.ApproachAttempts);
                return;
            }

            // Target moved away: Mobius re-issues moveToPawn and keeps the attack intention.
            long ms = ApproachTarget(player, target, PlayerReach(player, target));
            if (ms < 0)
            {
                player.Intent.AutoAttackTargetId = 0;
                player.Intent.ClearApproach();
                SendActionFailed(player);
                return;
            }
            player.Intent.NextAutoAttackUnixMs = now + ms;
            return;
        }
        player.Intent.ClearApproach();
        player.Intent.NextAutoAttackUnixMs = now + PlayerAttackTimeMs(player.ObjectId);
        if (TryAttack(session, targetId, out _, forced: true, advanceMoves: false) != null)
            player.Intent.AutoAttackTargetId = 0;
    }

    static StatusUpdateMsg StatusFor(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
        Values = [entity.CurHp, entity.MaxHp]
    };

    readonly List<PendingMeleeHit> _meleeHits = new();

    /// <summary>
    /// Queues a swing for <see cref="TickMeleeHits"/>. The list is world bookkeeping: inside a region body the add
    /// waits for the stage barrier, anywhere else it lands at once.
    /// </summary>
    void QueueMeleeHit(PendingMeleeHit hit) => AtBarrier(() => _meleeHits.Add(hit));

    void QueueAttackerDamageLine(ZoneEntity attacker, ZoneEntity target, int damage, byte flags = 0)
    {
        // Mobius Player.sendDamageMessage: YOU_HAVE_MISSED / CRITICAL_HIT / YOU_HIT_FOR_S1_DAMAGE go to the attacker
        // only, never to watchers (they read the numbers from the Attack packet).
        if (attacker.Session == null || NetBridge == null)
            return;
        if ((flags & ZoneCombat.HitFlagMiss) != 0)
        {
            NetBridge.FanoutSystemMessage([attacker.ObjectId], Localize(attacker, CombatLines.Missed()));
            return;
        }
        if ((flags & ZoneCombat.HitFlagCrit) != 0)
            NetBridge.FanoutSystemMessage([attacker.ObjectId], Localize(attacker, CombatLines.CriticalHit()));
        if (damage <= 0)
            return;

        NetBridge.FanoutSystemMessage(
            [attacker.ObjectId],
            Localize(attacker, CombatLines.HitTarget(target.Name, damage)));
    }

    /// <summary>
    /// Victim side of a melee / skill hit (Mobius PlayerStatus.reduceHp S1_HIT_YOU_FOR_S2_DAMAGE,
    /// Creature.sendDamageMessage YOU_HAVE_AVOIDED_S1_S_ATTACK, Formulas.calcShldUse shield line). Victim only.
    /// </summary>
    void SendVictimHitLines(ZoneEntity attacker, ZoneEntity victim, int damage, byte flags)
    {
        if (victim.Kind != ZoneEntityKind.Player || victim.Session == null || NetBridge == null)
            return;
        if ((flags & ZoneCombat.HitFlagMiss) != 0)
        {
            NetBridge.FanoutSystemMessage([victim.ObjectId], Localize(victim, CombatLines.AvoidedAttack(attacker.Name)));
            return;
        }
        if ((flags & ZoneCombat.HitFlagShield) != 0)
            NetBridge.FanoutSystemMessage([victim.ObjectId], Localize(victim, CombatLines.ShieldDefense()));
        if (damage > 0)
            NetBridge.FanoutSystemMessage([victim.ObjectId], Localize(victim, CombatLines.HitYou(attacker.Name, damage)));
    }

    /// <summary>
    /// Mobius <c>Creature.broadcastPacket</c> audience for an actor's visual (Attack, MagicSkillUse, MagicSkillLaunched,
    /// MagicSkillCanceled): the actor itself plus every player whose knownlist holds the actor. A player target is
    /// always included (it is necessarily in the actor's AOI when the packet is legal).
    /// </summary>
    List<int>? ActorAudience(ZoneEntity actor, ZoneEntity? target = null) => ActorNetWatchers(actor, target);

    void FanoutAttack(ZoneEntity attacker, ZoneEntity target, AttackMsg attack)
    {
        if (ActorAudience(attacker, target) is { Count: > 0 } netWatchers && NetBridge != null)
            NetBridge.FanoutAttackState(netWatchers, attack);
    }

    /// <summary>Mobius MagicSkillLaunched broadcast at hitTime.</summary>
    void FanoutSkillLaunched(ZoneEntity caster, int skillId, int level, params ZoneEntity[] targets)
    {
        if (skillId <= 0 || NetBridge == null)
            return;
        if (ActorAudience(caster, targets.Length > 0 ? targets[0] : null) is { Count: > 0 } netWatchers)
            NetBridge.FanoutMagicSkillLaunched(netWatchers, caster.ObjectId, skillId, Math.Max(1, level),
                targets.Select(t => t.ObjectId).ToArray());
    }
}
