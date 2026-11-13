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
        // Pending pickups / corpse loot may dirty inventories — keep Postgres off _gate (B8).
        WithDeferredInventoryPersist(() =>
        {
            long now = _clock().ToUnixTimeMilliseconds();
            TickPlayerMovement(now);
            TickNpcMovement(now);
            TickRandomWalk(now);
            TickPendingHits(now);
            TickMeleeHits(now);
            TickPvpFlags(now);
            TickAutoAttacks();
            TickPendingCasts(now);
            TickQueuedMoves(now);
            TickPendingPickups(now);
            TickPendingTalks(now);
            TickFear(now);
            // Proactive AI used to live only on RunCombatTickAsync (tests). Without these,
            // aggressive mobs never aggro or leash on the live CombatLoopAsync path.
            TickAggression(now);
            TickLeash(now);
            TickMobAttacks(now);
            TickCorpseDecay(now);
            TickAttackStance(now);
            TickGroundExpiry(now);
            return 0;
        });
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
            if (player.Sitting || IsActionBlocked(player) || IsPhysicallyMuted(player) ||
                HostilePlayableRefused(session, player, target, badSkill: false))
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
}
