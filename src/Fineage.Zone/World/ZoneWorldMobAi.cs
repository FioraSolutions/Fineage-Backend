using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

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
        RunStep(now =>
        {
            TickPlayerMovement(now);
            TickNpcMovement(now);
            RunNpcThinkTick(now);
            _simulationTicks.RunNow(_pendingHitStage, now);
            TickMeleeHits(now);
            _simulationTicks.RunNow(_aggressionStage, now);
            TickLeash(now);
            _simulationTicks.RunNow(_mobAttackStage, now);
            TickSummons(now);
            TickCubics(now);
            TickDuels(now);
            TickCorpseDecay(now);
            TickQuestSpawns(now);
            TickSignets(now);
            TickSkillChannels(now);
            TickAttackStance(now);
        });

        return Task.CompletedTask;
    }

    void CollectPendingHits(TickContext tick, RegionWorkCollector work) => _byId.Collect(ActiveSets.PendingHit, work);

    void PendingHitEntity(int id, long now)
    {
        if (!_byId.TryGetValue(id, out var entity))
            return;
        if (entity.PendingHit == null)
        {
            _byId.Leave(entity, ActiveSets.PendingHit);
            return;
        }
        if (entity.PendingHit.DueUnixMs > now)
            return;
        if (entity.Kind == ZoneEntityKind.Player)
            ResolvePendingHit(entity, now);
        else if (entity.Kind == ZoneEntityKind.Npc)
            ResolveNpcPendingHit(entity, now);
    }

    void ResolvePendingHit(ZoneEntity player, long now)
    {
        var hit = player.PendingHit;
        SetPendingHit(player, null);
        if (hit == null || player.Dead)
            return;
        if (hit.Apply is DeferredEffect)
        {
            ResolveDeferredEffect(player, hit, now);
            return;
        }
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.CurHp <= 0 ||
            target.Kind is not (ZoneEntityKind.Npc or ZoneEntityKind.Player))
        {
            AbortCast(player, "target-gone", notify: false);
            return;
        }

        // Mobius onMagicLaunchedTimer: target escaped (effectRange) or out of sight at launch → abortCast, mpConsume not spent.
        if (hit.Combat != null)
        {
            int escape = Fineage.Game.Combat.CastRules.EscapeRange(hit.Combat.CastRange, hit.Combat.EffectRange);
            if (escape > 0 && !ZoneCombat.Within(player.X, player.Y, target.X, target.Y, SkillCastReach(escape, player, target)))
            {
                AbortCast(player, "escaped", notify: false);
                return;
            }

            if (!CanSeeTarget(player, target))
            {
                AbortCast(player, "no-los", notify: false);
                return;
            }

            if (!LaunchConsumeItems(player.Session, player, hit))
            {
                AbortCast(player, "item-consume", notify: false);
                return;
            }

            // Mobius onMagicLaunchedTimer → broadcastPacket(MagicSkillLaunched) to the caster's knownlist.
            FanoutSkillLaunched(player, hit.Combat.SkillId, hit.Level, target);
            ApplyFocusEnergyAfterCombatHit(player, hit.Combat.SkillId, hit.Level);
        }

        int damage = hit.Combat != null ? RollSkillDamage(player, target, hit.Combat, hit.Level) : hit.Damage;
        if (hit.Combat != null)
        {
            ZoneHotPathLogs.MobSkillHit(hit.Combat.SkillId, hit.Level, target.ObjectId, damage);
            // Mobius Player.sendDamageMessage after a damage skill: the caster sees its own number.
            if (damage > 0)
                QueueAttackerDamageLine(player, target, damage);
        }

        // The target may be owned by another region: HP, hate, death and rewards travel as region messages and land
        // at the stage barrier (outside a body, Post applies them at once).
        Post(new SkillDamageMessage(player.ObjectId, target.ObjectId, damage, hit.Combat?.SkillId ?? 0, hit.Level, now));

        // Debuff-nukes (Aura Flare, Fire Vortex, …): the abnormal lands with the damage at hitTime. Mobius applies
        // it to a playable only when the hit dealt damage; a monster takes it either way.
        if (hit.Combat != null && target.Summon == null && (damage > 0 || target.Kind == ZoneEntityKind.Npc) &&
            _game.Skills.Find(hit.Combat.SkillId) is { AppliesAbnormal: true } nuke &&
            nuke.TryLevelData(hit.Level, out _))
        {
            Post(new AbnormalApplyMessage(player.ObjectId, target.ObjectId, hit.Combat.SkillId, hit.Level, now));
        }
    }

    /// <summary>
    /// The target side of a player's skill hit (Mobius <c>onMagicHitTimer</c>): HP, hate, drain, death and rewards.
    /// Runs where the target may be written: at the stage barrier (<see cref="SkillDamageMessage"/>) or anywhere a
    /// region body is not running.
    /// </summary>
    void ApplyPlayerSkillDamage(ZoneEntity player, ZoneEntity target, int damage, int skillId, int level, long now)
    {
        // Two pending hits due in the same tick both reach the barrier before either is applied; the first one's
        // death flag makes the second a no-op instead of a second death/drop/reward roll.
        if (target.Dead)
            return;
        if (target.Summon != null)
        {
            UpdatePvPStatus(player, CreditOf(target), now);
            DamageSummon(player, target, damage, now);
            if (skillId > 0 && damage > 0)
                ApplyHpDrainAfterDamage(player, target, damage, skillId);
            return;
        }
        if (target.Kind == ZoneEntityKind.Player)
        {
            ApplySkillDamageToPlayer(player, target, damage, now);
            if (skillId > 0 && damage > 0)
                ApplyHpDrainAfterDamage(player, target, damage, skillId);
            return;
        }
        if (SiegeNpcHit(player, target, damage, now))
            return;
        // Mobius FolkStatus: offensive skills may cast on merchants, but they take no HP / hate.
        if (!ZoneCombat.TakesNpcCombatDamage(target.NpcType, target.Attackable))
        {
            player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            return;
        }
        if (TryExplodeTreasureBox(player, target, now))
            return;
        DuelNoteOutsideHit(player, target);
        bool magicSkill = (skillId > 0 && _skillCombat.TryGet(skillId, out var combatData) && combatData is { Kind: SkillDamageKind.Magical })
                          || (skillId > 0 && _game.Skills.Find(skillId) is { MagicType: 1 });
        if (TryApplyRaidCurse(target, player, magicSkill, now))
            return;

        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, StatusFor(target));
        if (skillId > 0 && damage > 0)
            ApplyHpDrainAfterDamage(player, target, damage, skillId);
        RecordDamage(target, player, damage);
        ApplyQuestAttack(player, target);
        AddHate(target, player, DamageHate(damage, target.Level));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;

        if (target.CurHp <= 0 && RemoveDeadNpc(target, killer: player))
            GrantKillReward(player, target);
    }

    /// <summary>Offensive skill landing on a player: HP, purple name, cast break, death. Same gate as a melee hit.</summary>
    void ApplySkillDamageToPlayer(ZoneEntity attacker, ZoneEntity target, int damage, long now)
    {
        // Mobius callSkill: an offensive skill on a playable flags the caster whether it landed for damage or not.
        if (attacker.Kind == ZoneEntityKind.Player)
            UpdatePvPStatus(attacker, target, now);
        if (damage <= 0)
        {
            attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            return;
        }

        if (DuelBlocksHit(attacker, target))
            return;
        if (target.Invulnerable)
            return;
        // Mobius CreatureAI.checkTargetLostOrDead with Config.FAKE_DEATH_DAMAGE_STAND=true (CT0 default): a hit on a
        // playing-dead pawn stands it up (stopFakeDeath(true)) instead of killing the corpse.
        if (target.FakeDeath)
            WakeUpFromFakeDeath(target, "damage");
        target.CurHp = Math.Max(0, target.CurHp - damage);
        ApplyReflectAndAbsorb(attacker, target, damage);
        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        FanoutVitals(target, PlayerVitalsStatus(target));
        SendVictimHitLines(attacker, target, damage, 0);
        // Mobius onCreatureDamageReceived → TriggerSkillByDamage (Mirage 445).
        RunDamageTriggers(target, attacker, damage);
        if (target.CurHp > 0)
        {
            MaybeBreakCast(target, damage);
            return;
        }

        if (target.Session != null)
            target.Intent.AutoAttackTargetId = 0;
        ApplyPlayerDeath(target, attacker.ObjectId);
    }

    void CollectAiBusy(TickContext tick, RegionWorkCollector work) => _byId.Collect(ActiveSets.AiBusy, work);

    void MobAttackEntity(int id, long now)
    {
        if (!_byId.TryGetValue(id, out var npc) || npc.Kind != ZoneEntityKind.Npc || npc.AggroTargetId <= 0 || npc.Summon != null ||
            IsTreasureBox(npc.NpcId))
            return;
        if (npc.CurHp <= 0)
        {
            SetAggroTarget(npc, 0);
            npc.HateList.Clear();
            StopTimedMove(npc);
            return;
        }
        // Mobius thinkAttack: attack timeout (2 min without a fresh onEvtAttacked) drops the fight
        // and teleports a monster back to its spawn. The teleport relocates: it runs at the stage barrier.
        if (npc.AttackTimeoutAtMs > 0 && now >= npc.AttackTimeoutAtMs)
        {
            AtBarrier(() => TeleportHome(npc));
            return;
        }
        if (!_byId.TryGetValue(npc.AggroTargetId, out var player) ||
            !IsValidMobAttackTarget(npc, player) ||
            !SameInstance(npc.ObjectId, player.ObjectId) ||
            !ZoneCombat.WithinAggroForgetRange(npc.X, npc.Y, player.X, player.Y))
        {
            // Mobius AggroList: drop the gone target, fall to the next most hated; none left -> returnHome.
            npc.HateList.Remove(npc.AggroTargetId);
            int next = MostHated(npc);
            if (next > 0)
            {
                SetAggroTarget(npc, next);
                npc.NextMobAttackMs = now + ZoneCombat.HitTimeMs(NpcPAtkSpd(npc));
                return;
            }
            ReturnHome(npc, now);
            return;
        }
        int topHate = MostHated(npc);
        if (topHate > 0)
            SetAggroTarget(npc, topHate);
        bool summonTarget = player.Summon != null;
        if (!summonTarget)
            CallClanHelp(npc, player, now);
        npc.LastAiTickMs = now;
        // Mobius onEvtAfraid: the first step is away from the caster; later steps follow heading.
        if (IsFeared(npc))
        {
            if (now >= npc.FearFleeAtUnixMs)
                StartFearFlee(npc, from: null, alongHeading: true);
            return;
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
            return;
        }
        // Mobius thinkAttack: no LOS to the most hated → moveTo(target) instead of swinging.
        if (!ZoneCombat.Within(npc.X, npc.Y, player.X, player.Y, AttackReachAgainst(npc, player)) ||
            !CanSeeTarget(npc, player))
        {
            // Mobius AttackableAI.thinkAttack -> moveToPawn(target, range): mob runs to the attacker.
            ChaseStep(npc, player, now);
            return;
        }
        if (npc.Chasing)
        {
            npc.Chasing = false;
            // Arrived: halt at the server position; Mobius sends a final MoveToLocation/StopMove at the reached spot.
            StopTimedMove(npc);
            BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z);
        }
        if (now < npc.NextMobAttackMs || npc.CastingUntilMs > now)
            return;
        // Mobius thinkAttack: SUICIDE (HP&lt;30%), then HEAL/BUFF/control/debuff, then ATTACK skill chance.
        // Against a summon only the melee swing is used here (NPC skill paths roll against a player).
        if (summonTarget)
        {
            int swingMs = ZoneCombat.AttackTimeMs(NpcPAtkSpd(npc));
            npc.NextMobAttackMs = now + swingMs;
            var (sDamage, sFlags) = NpcMeleeRoll(npc, player);
            // The victim may be owned by another region: the combat-stance write lands at the stage barrier.
            AtBarrier(() => player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis);
            FanoutAttack(npc, player, new AttackMsg
            {
                AttackerObjectId = npc.ObjectId,
                TargetObjectId = player.ObjectId,
                Damage = sDamage,
                Flags = sFlags,
                AttackerX = npc.X,
                AttackerY = npc.Y,
                AttackerZ = npc.Z,
                TargetX = player.X,
                TargetY = player.Y,
                TargetZ = player.Z
            });
            QueueMeleeHit(new PendingMeleeHit(npc.ObjectId, player.ObjectId, sDamage, now + swingMs / 2, sFlags));
            return;
        }
        if (TryNpcSuicideSkill(npc, player, now))
            return;
        if (TryNpcHealSkill(npc, now))
            return;
        if (TryNpcSelfBuffSkill(npc, now))
            return;
        if (TryNpcImmobilizeSkill(npc, player, now))
            return;
        if (TryNpcSilenceSkill(npc, player, now))
            return;
        if (TryNpcDebuffSkill(npc, player, now))
            return;
        // Mobius AttackableAI: skill chance before the melee swing.
        if (TryNpcSkillInsteadOfMelee(npc, player, now))
            return;
        int npcAtkMs = ZoneCombat.AttackTimeMs(NpcPAtkSpd(npc));
        npc.NextMobAttackMs = now + npcAtkMs;

        var (damage, flags) = NpcMeleeRoll(npc, player);
        AtBarrier(() => player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis);

        var attack = new AttackMsg
        {
            AttackerObjectId = npc.ObjectId,
            TargetObjectId = player.ObjectId,
            Damage = damage,
            Flags = flags,
            AttackerX = npc.X,
            AttackerY = npc.Y,
            AttackerZ = npc.Z,
            TargetX = player.X,
            TargetY = player.Y,
            TargetZ = player.Z
        };
        // Swing now; player HP drops at hitTime (Mobius onHitTimer).
        FanoutAttack(npc, player, attack);
        QueueMeleeHit(new PendingMeleeHit(npc.ObjectId, player.ObjectId, damage, now + npcAtkMs / 2, flags));
    }

}
