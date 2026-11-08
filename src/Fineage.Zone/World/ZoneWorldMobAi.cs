using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius-style hit timing and mob retaliation.
/// Mobius: Creature.onMagicHitTimer applies skill damage at hitTime;
/// Attackable.addDamageHate + AttackableAI sets intention ATTACK on the attacker, the mob swings at Pspd until target dies or leaves.
/// </summary>
public sealed partial class ZoneWorld
{
    internal Task RunCombatTickAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            TickPlayerMovement(now);
            TickNpcMovement(now);
            TickPendingHits(now);
            TickMeleeHits(now);
            TickAggression(now);
            TickLeash(now);
            TickMobAttacks(now);
            TickCorpseDecay(now);
            TickAttackStance(now);
        }

        return Task.CompletedTask;
    }

    void TickPendingHits(long now)
    {
        if (_pendingHit.Count == 0)
            return;
        foreach (int id in _pendingHit.ToArray())
        {
            if (!_byId.TryGetValue(id, out var entity) || entity.PendingHit == null)
            {
                _pendingHit.Remove(id);
                continue;
            }
            if (entity.PendingHit.DueUnixMs > now)
                continue;
            if (entity.Kind == ZoneEntityKind.Player)
                ResolvePendingHit(entity, now);
            else if (entity.Kind == ZoneEntityKind.Npc)
                ResolveNpcPendingHit(entity, now);
        }
    }

    void ResolvePendingHit(ZoneEntity player, long now)
    {
        var hit = player.PendingHit;
        SetPendingHit(player, null);
        if (hit == null || player.Dead)
            return;
        if (hit.Apply is DeferredEffect deferred)
        {
            ResolveDeferredEffect(player, hit, deferred, now);
            return;
        }
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.Kind != ZoneEntityKind.Npc || target.CurHp <= 0)
        {
            AbortCast(player, "target-gone", notify: false);
            return;
        }

        // Mobius onMagicLaunchedTimer: target escaped (effectRange) or out of sight at launch → abortCast, mpConsume not spent.
        if (hit.Combat != null)
        {
            int escape = Fineage.Game.Combat.CastRules.EscapeRange(hit.Combat.CastRange, hit.Combat.EffectRange);
            if (escape > 0 && !ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, escape + ZoneCombat.HitRangeTolerance))
            {
                AbortCast(player, "escaped", notify: false);
                return;
            }

            if (!CanSeeTarget(player, target))
            {
                AbortCast(player, "no-los", notify: false);
                return;
            }
        }

        int damage = hit.Combat != null ? RollSkillDamage(player, target, hit.Combat, hit.Level) : hit.Damage;
        if (hit.Combat != null)
        {
            ZoneLog.Info($"[skill] hit {hit.Combat.SkillId} lv{hit.Level} target={target.ObjectId} damage={damage}");
        }
        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, StatusFor(target));
        RecordDamage(target, player, damage);
        AddHate(target, player, DamageHate(damage, target.Level));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;

        if (target.CurHp <= 0)
        {
            RemoveDeadNpc(target, killer: player);
            GrantKillReward(player, target);
        }
    }

    void TickMobAttacks(long now)
    {
        if (_aiBusy.Count == 0)
            return;
        foreach (int id in _aiBusy.ToArray())
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc || npc.AggroTargetId <= 0)
                continue;
            if (npc.CurHp <= 0)
            {
                SetAggroTarget(npc, 0);
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
                    SetAggroTarget(npc, next);
                    npc.NextMobAttackMs = now + ZoneCombat.HitTimeMs(NpcPAtkSpd(npc));
                    continue;
                }
                ReturnHome(npc, now);
                continue;
            }
            int topHate = MostHated(npc);
            if (topHate > 0)
                SetAggroTarget(npc, topHate);
            CallClanHelp(npc, player, now);
            npc.LastAiTickMs = now;
            // Mobius thinkAttack: no LOS to the most hated → moveTo(target) instead of swinging.
            if (!ZoneCombat.InMobReach(npc.X, npc.Y, player.X, player.Y, ZoneCombat.MobAttackReach(npc.AttackRange)) ||
                !CanSeeTarget(npc, player))
            {
                // Mobius AttackableAI.thinkAttack -> moveToPawn(target, range): mob runs to the attacker.
                ChaseStep(npc, player, now);
                continue;
            }
            if (npc.Chasing)
            {
                npc.Chasing = false;
                // Arrived: halt at the server position; Mobius sends a final MoveToLocation/StopMove at the reached spot.
                StopTimedMove(npc);
                BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z);
            }
            if (now < npc.NextMobAttackMs || npc.CastingUntilMs > now)
                continue;
            // Mobius thinkAttack: SUICIDE (HP&lt;30%), then HEAL/BUFF/control/debuff, then ATTACK skill chance.
            if (TryNpcSuicideSkill(npc, player, now))
                continue;
            if (TryNpcHealSkill(npc, now))
                continue;
            if (TryNpcSelfBuffSkill(npc, now))
                continue;
            if (TryNpcImmobilizeSkill(npc, player, now))
                continue;
            if (TryNpcSilenceSkill(npc, player, now))
                continue;
            if (TryNpcDebuffSkill(npc, player, now))
                continue;
            // Mobius AttackableAI: skill chance before the melee swing.
            if (TryNpcSkillInsteadOfMelee(npc, player, now))
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
            FanoutAttack(npc, player, attack);
            _meleeHits.Add(new PendingMeleeHit(npc.ObjectId, player.ObjectId, damage, now + npcAtkMs / 2));
        }
    }

    /// <summary>
    /// One AI tick of chase (Mobius moveToPawn). Only (re)issues a MoveToLocation when the chase starts or the
    /// pawn drifted; the actual travel is done by <see cref="TickNpcMovement"/> on the shared speed-tick mover.
    /// </summary>
    void ChaseStep(ZoneEntity npc, ZoneEntity target, long now)
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
            SetReturningHome(npc, false);
            if (!npc.Running)
            {
                // Mobius AttackableAI: setRunning() on attack intention -> ChangeMoveType(run).
                npc.Running = true;
                FanoutMoveType(npc);
            }
            npc.Chasing = true;
            npc.ChaseDestX = destX;
            npc.ChaseDestY = destY;
            StartNpcMove(npc, destX, destY, destZ, now);
        }
    }

    void BroadcastNpcMove(ZoneEntity npc, int destX, int destY, int destZ)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session != null && watcher.Knownlist.Contains(npc.ObjectId))
            {
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
    void TickCorpseDecay(long now)
    {
        if (_decaying.Count == 0)
            return;
        foreach (int id in _decaying.ToArray())
        {
            if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc || npc.DecayAtMs <= 0)
            {
                _decaying.Remove(id);
                continue;
            }
            if (npc.DecayAtMs > now)
                continue;
            SetDecayAtMs(npc, 0);
            DespawnNpc(npc);
        }
    }
}
