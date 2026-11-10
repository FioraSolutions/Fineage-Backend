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
        WithDeferredInventoryPersist(() =>
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
            return 0;
        });

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
        // Mobius FolkStatus: offensive skills may cast on merchants, but they take no HP / hate.
        if (!ZoneCombat.IsCombatNpcType(target.NpcType))
        {
            player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            return;
        }
        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, StatusFor(target));
        RecordDamage(target, player, damage);
        AddHate(target, player, DamageHate(damage, target.Level));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;

        // Debuff-nukes (Aura Flare, Fire Vortex, …): abnormal lands with the damage at hitTime.
        if (hit.Combat != null)
        {
            var template = _game.Skills.Find(hit.Combat.SkillId);
            if (template != null &&
                template.AppliesAbnormal &&
                template.TryLevelData(hit.Level, out var levelData))
            {
                ApplyAbnormalFromSkill(template, levelData, target, now, attacker: player);
            }
        }

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
            // Mobius Fear / TURN_FLEE: skip attack AI and run away from the feared source.
            if (IsFeared(npc))
            {
                FleeFrom(npc, player, now);
                continue;
            }
            // Sleep / stun / root / hold / paralyze: stop chase and do not swing (Mobius EffectFlag).
            if (IsImmobilized(npc))
            {
                if (npc.Chasing || npc.MoveActive)
                {
                    npc.Chasing = false;
                    StopTimedMove(npc);
                    BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z);
                }
                continue;
            }
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

}
