using Fineage.Protocol;
using Fineage.Zone.Combat;
using MessagePack;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius-style hit timing and mob retaliation.
/// Mobius: Creature.onMagicHitTimer applies skill damage at hitTime;
/// Attackable.addDamageHate + AttackableAI sets intention ATTACK on the attacker, the mob swings at Pspd until target dies or leaves.
/// </summary>
public sealed partial class ZoneWorld
{
    internal async Task RunCombatTickAsync(CancellationToken ct)
    {
        var outbound = new List<Pending>();
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            TickPlayerMovement(now, outbound);
            TickNpcMovement(now, outbound);
            TickPendingHits(now, outbound);
            TickMeleeHits(now, outbound);
            TickAggression(now);
            TickMobAttacks(now, outbound);
            TickCorpseDecay(now, outbound);
            TickAttackStance(now);
        }
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
    }

    void TickPendingHits(long now, List<Pending> outbound)
    {
        foreach (var player in Players().ToList())
        {
            if (player.PendingHit != null && player.PendingHit.DueUnixMs <= now)
                ResolvePendingHit(player, now, outbound);
        }
    }

    void ResolvePendingHit(ZoneEntity player, long now, List<Pending> outbound)
    {
        var hit = player.PendingHit;
        player.PendingHit = null;
        if (hit == null || player.Dead)
            return;
        if (hit.Apply is DeferredEffect deferred)
        {
            ResolveDeferredEffect(player, hit, deferred, now, outbound);
            return;
        }
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.Kind != ZoneEntityKind.Npc || target.CurHp <= 0)
        {
            AbortCast(player, "target-gone", outbound, notify: false);
            return;
        }

        // Mobius onMagicLaunchedTimer: target escaped (effectRange) or out of sight at launch → abortCast, mpConsume not spent.
        if (hit.Combat != null)
        {
            int escape = Fineage.Game.Combat.CastRules.EscapeRange(hit.Combat.CastRange, hit.Combat.EffectRange);
            if (escape > 0 && !ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, escape + ZoneCombat.HitRangeTolerance))
            {
                AbortCast(player, "escaped", outbound, notify: false);
                return;
            }

            if (!CanSeeTarget(player, target))
            {
                AbortCast(player, "no-los", outbound, notify: false);
                return;
            }
        }

        int damage = hit.Combat != null ? RollSkillDamage(player, target, hit.Combat, hit.Level, outbound) : hit.Damage;
        if (hit.Combat != null)
        {
            ZoneLog.Info($"[skill] hit {hit.Combat.SkillId} lv{hit.Level} target={target.ObjectId} damage={damage}");
            if (player.Session != null)
                outbound.Add(Pack(player.Session, MsgType.StatusUpdate, CasterMpStatus(player)));
        }
        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, StatusFor(target), outbound);
        AddHate(target, player, DamageHate(damage, target.Level));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;

        if (target.CurHp <= 0)
        {
            RemoveDeadNpc(target, outbound, killer: player);
            GrantKillReward(player, target, outbound);
        }
    }

    void TickMobAttacks(long now, List<Pending> outbound)
    {
        foreach (var npc in _byId.Values.Where(e => e.Kind == ZoneEntityKind.Npc && e.AggroTargetId > 0).ToList())
        {
            if (npc.CurHp <= 0)
            {
                npc.AggroTargetId = 0;
                npc.HateList.Clear();
                StopTimedMove(npc);
                continue;
            }
            if (!_byId.TryGetValue(npc.AggroTargetId, out var player) ||
                player.Kind != ZoneEntityKind.Player || player.Dead || player.Session == null ||
                !SameInstance(npc.ObjectId, player.ObjectId) ||
                !ZoneCombat.InAggroRange(npc.X, npc.Y, player.X, player.Y))
            {
                // Mobius AggroList: drop the gone target, fall to the next most hated; none left -> returnHome.
                npc.HateList.Remove(npc.AggroTargetId);
                int next = MostHated(npc);
                if (next > 0)
                {
                    npc.AggroTargetId = next;
                    npc.NextMobAttackMs = now + ZoneCombat.HitTimeMs(NpcPAtkSpd(npc));
                    continue;
                }
                ReturnHome(npc, now, outbound);
                continue;
            }
            npc.AggroTargetId = MostHated(npc) is > 0 and var top ? top : npc.AggroTargetId;
            CallClanHelp(npc, player, now);
            npc.LastAiTickMs = now;
            // Mobius thinkAttack: no LOS to the most hated → moveTo(target) instead of swinging.
            if (!ZoneCombat.InMobReach(npc.X, npc.Y, player.X, player.Y, ZoneCombat.MobAttackReach(npc.AttackRange)) ||
                !CanSeeTarget(npc, player))
            {
                // Mobius AttackableAI.thinkAttack -> moveToPawn(target, range): mob runs to the attacker.
                ChaseStep(npc, player, now, outbound);
                continue;
            }
            if (npc.Chasing)
            {
                npc.Chasing = false;
                // Arrived: halt at the server position; Mobius sends a final MoveToLocation/StopMove at the reached spot.
                StopTimedMove(npc);
                BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z, outbound);
            }
            if (now < npc.NextMobAttackMs)
                continue;
            int npcAtkMs = ZoneCombat.AttackTimeMs(NpcPAtkSpd(npc));
            npc.NextMobAttackMs = now + npcAtkMs;

            var (damage, flags) = NpcMeleeRoll(npc, player);
            player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;

            var attack = new AttackMsg
            {
                AttackerObjectId = npc.ObjectId,
                TargetObjectId = player.ObjectId,
                Damage = damage,
                Flags = flags,
                AttackerX = npc.X, AttackerY = npc.Y, AttackerZ = npc.Z,
                TargetX = player.X, TargetY = player.Y, TargetZ = player.Z
            };
            // Swing now; player HP drops at hitTime (Mobius onHitTimer).
            FanoutAttack(npc, player, attack, outbound);
            _meleeHits.Add(new PendingMeleeHit(npc.ObjectId, player.ObjectId, damage, now + npcAtkMs / 2));
        }
    }

    /// <summary>
    /// One AI tick of chase (Mobius moveToPawn). Only (re)issues a MoveToLocation when the chase starts or the
    /// pawn drifted; the actual travel is done by <see cref="TickNpcMovement"/> on the shared speed-tick mover.
    /// </summary>
    void ChaseStep(ZoneEntity npc, ZoneEntity target, long now, List<Pending> outbound)
    {
        double dx = target.X - npc.X;
        double dy = target.Y - npc.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double stopAt = ZoneCombat.MobAttackReach(npc.AttackRange) - ZoneCombat.ApproachSlack;
        if (dist <= stopAt)
            return;

        // Destination: point at stopAt from the target on the npc side (Mobius moveToPawn offset).
        int destX = (int)Math.Round(target.X - dx / dist * stopAt);
        int destY = (int)Math.Round(target.Y - dy / dist * stopAt);
        int destZ = target.Z;

        long ddx = destX - (long)npc.ChaseDestX, ddy = destY - (long)npc.ChaseDestY;
        if (!npc.Chasing || !npc.MoveActive || ddx * ddx + ddy * ddy > 40L * 40L)
        {
            if (!npc.HomeSet)
            {
                npc.HomeSet = true;
                (npc.HomeX, npc.HomeY, npc.HomeZ) = (npc.X, npc.Y, npc.Z);
            }
            npc.ReturningHome = false;
            if (!npc.Running)
            {
                // Mobius AttackableAI: setRunning() on attack intention -> ChangeMoveType(run).
                npc.Running = true;
                FanoutMoveType(npc, outbound);
            }
            npc.Chasing = true;
            npc.ChaseDestX = destX;
            npc.ChaseDestY = destY;
            StartNpcMove(npc, destX, destY, destZ, now, outbound);
        }
    }

    void BroadcastNpcMove(ZoneEntity npc, int destX, int destY, int destZ, List<Pending> outbound)
    {
        byte[] body = MessagePackSerializer.Serialize(new MoveToMsg
        {
            ObjectId = npc.ObjectId,
            DestX = destX, DestY = destY, DestZ = destZ,
            OriginX = npc.X, OriginY = npc.Y, OriginZ = npc.Z
        });
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session != null && watcher.Knownlist.Contains(npc.ObjectId))
            {
                outbound.Add(new Pending(watcher.Session, MsgType.MoveToAck, body));
                netWatchers ??= [];
                netWatchers.Add(watcher.ObjectId);
            }
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutMoveState(
                netWatchers,
                npc.ObjectId,
                npc.X, npc.Y, npc.Z,
                destX, destY, destZ,
                npc.Running);
        }
    }

    /// <summary>Mobius DecayTaskManager: corpse stays (death animation) then DeleteObject + respawn timer.</summary>
    void TickCorpseDecay(long now, List<Pending> outbound)
    {
        foreach (var npc in _byId.Values.Where(e => e.Kind == ZoneEntityKind.Npc && e.DecayAtMs > 0 && e.DecayAtMs <= now).ToList())
        {
            npc.DecayAtMs = 0;
            DespawnNpc(npc, outbound);
        }
    }
}
