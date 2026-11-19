using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TrySkillUseSupport(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = ResolveFriendlyTargetId(session, player, request, template);
        if (targetId <= 0)
        {
            IncorrectTarget(session);
            return null;
        }
        if (!_byId.TryGetValue(targetId, out var target))
        {
            IncorrectTarget(session);
            return null;
        }
        if (!IsFriendlySkillTarget(player, target, request.CtrlPressed) || target.Dead)
        {
            IncorrectTarget(session);
            return null;
        }
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
        {
            IncorrectTarget(session);
            return null;
        }
        if (template.CastRange > 0 &&
            targetId != player.ObjectId &&
            !ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        if (levelData.Power <= 0)
            throw new InvalidOperationException(
                $"skill {request.SkillId} lv{level} heal power missing — refuse invented heal amount");
        int heal = levelData.Power;
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        FlagForcedSupport(player, target, request.CtrlPressed, now);
        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        // Mobius Heal effect at hitTime: HP rises when the cast completes, not when it starts.
        ScheduleEffect(player, target, template, level, now, hitMs, (t, _) =>
        {
            if (heal > 0 && t.MaxHp > 0)
                t.CurHp = Math.Min(t.MaxHp, t.CurHp + heal);
            FanoutVitals(t, new StatusUpdateMsg
            {
                ObjectId = t.ObjectId,
                AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
                Values = [t.CurHp, t.MaxHp]
            });
        });
        return null;
    }

    /// <summary>Mobius SystemMessageId 1510 — ConfirmDlg text for skill resurrection.</summary>
    public const int SysMsgResurrectionOffer = 1510;

    /// <summary>Mobius SystemMessageId 1513 RESURRECTION_HAS_ALREADY_BEEN_PROPOSED.</summary>
    public const int SysMsgResurrectionAlreadyProposed = 1513;

    /// <summary>
    /// Mobius Resurrection effect → <c>Player.reviveRequest</c>: cast on a dead player corpse (PC_BODY),
    /// then the target gets ConfirmDlg with the restore-exp preview.
    /// </summary>
    string? TrySkillUseResurrection(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target))
        {
            IncorrectTarget(session);
            return null;
        }
        if (target.Summon?.Pet != null && target.Dead && VisibleTo(player.ObjectId, targetId))
            return TrySkillUseResurrectPet(session, player, target, request.SkillId, template, levelData, level, now,
                SkillResurrectRestorePercent(player, levelData.Power));
        if (target.Kind != ZoneEntityKind.Player || !target.Dead || ReferenceEquals(player, target))
        {
            IncorrectTarget(session);
            return null;
        }
        if (!VisibleTo(player.ObjectId, targetId))
        {
            IncorrectTarget(session);
            return null;
        }
        if (template.CastRange > 0 &&
            !ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;
        if (target.ReviveRequested)
        {
            ShotNotice(session, SysMsgResurrectionAlreadyProposed, "Resurrection has already been proposed.");
            return null;
        }

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        double power = SkillResurrectRestorePercent(player, levelData.Power);
        long restoreExp = 0;
        if (target.ExpBeforeDeath > target.Exp && power > 0d)
            restoreExp = (long)Math.Round((target.ExpBeforeDeath - target.Exp) * power / 100d);

        var (hitMs, reuseMs) = ScaledSkillTimes(player, template);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, hitMs);

        // Offer lands at hitTime (Mobius BuffInfo onStart for Resurrection).
        string reviverName = player.Name;
        ScheduleEffect(player, target, template, level, now, hitMs, (t, _) =>
        {
            if (!t.Dead || t.Kind != ZoneEntityKind.Player || t.Session == null)
                return;
            if (t.ReviveRequested)
                return;

            t.ReviveRequested = true;
            t.RevivePower = power;
            NetBridge?.FanoutSystemMessage([t.ObjectId], new SystemMessageMsg
            {
                Id = SysMsgResurrectionOffer,
                Text =
                    $"{reviverName} is making an attempt to resurrect you. If you choose this path, {restoreExp} experience points will be returned to you. Do you want to be resurrected?",
                Params =
                [
                    reviverName,
                    restoreExp.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ]
            });
        }, allowDeadTarget: true);
        return null;
    }

    string? TrySkillUseDebuff(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (!IsAttackable(player, target))
            return SkillProtocol.Target;
        // Mobius Player.useMagic: an offensive skill on a player that is not auto-attackable needs Ctrl.
        if (!request.CtrlPressed && NeedsForce(player, target))
            return null;
        if (HostilePlayableRefused(session, player, target, badSkill: true) ||
            SiegeFriendRefused(session, player, target) ||
            PeacePlayableRefused(session, player, target))
            return null;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;
        // Mobius callSkill: a negative skill on a playable flags the caster (landing or resisted).
        UpdatePvPStatus(player, target, now);

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = targetId;
        // Mobius onMagicHitTimer: debuff lands at hitTime; a dead/escaped/hidden target aborts it.
        // Hate on landing is Mobius `effectPoint`, not `power`: onMagicHitTimer runs
        // `addDamageHate(this, 0, -effectPoint)` for a negative point and `reduceHate(this, effectPoint)` for a
        // positive one, BEFORE the `<for>` payloads. Veil (106) has no effectPoint at all, so it writes no hate —
        // the old unconditional AddHate(power) is what made a Veil'd monster turn on the caster.
        // AREA (Poisonous Cloud): affect around the primary target (Mobius affectRange, typically 200).
        bool isArea = template.TargetType.Equals("AREA", StringComparison.OrdinalIgnoreCase);
        int affectRange = isArea ? ResolveAffectRange(template) : 0;
        int effectPoint = template.EffectPointAt(level);
        ScheduleEffect(player, target, template, level, now, hitMs, (t, at) =>
        {
            if (isArea)
            {
                ApplyAreaDebuff(player, t, template, levelData, at, affectRange);
                return;
            }
            if (!ApplyAbnormalFromSkill(template, levelData, t, at, attacker: player))
                return;
            if (t.Kind == ZoneEntityKind.Npc)
                ApplyEffectPointHate(player, t, effectPoint, template, request.SkillId);
            // Mobius `Passive` (Veil's whole payload) sets EffectFlag.PASSIVE: Monster.isAggressive() turns false, so
            // the victim stops starting fights. TURN_PASSIVE also skips the EVT_ATTACKED notify entirely, and the
            // pawn forgets the caster it was hitting.
            if (IsTurnPassive(template.AbnormalType))
                PacifyNpc(t, player, at);
            RunInstantPayloadEffects(player, t, request.SkillId);
        });
        return null;
    }

    /// <summary>
    /// Mobius hostile A1 utility: Trick (11) and Switch (12) carry no abnormal at all — their whole payload is the
    /// <c>&lt;for&gt;</c> block (<c>DeleteHateOfMe</c> / <c>RandomizeHate</c> / <c>TargetCancel</c>) plus the negative
    /// <c>effectPoint</c> hate. Same target/range/refusal gates as a debuff, but nothing is applied to the abnormal
    /// bar; the instant payloads run when the hit lands.
    /// </summary>
    string? TrySkillUseHostileUtility(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (!IsAttackable(player, target))
            return SkillProtocol.Target;
        if (!request.CtrlPressed && NeedsForce(player, target))
            return null;
        if (HostilePlayableRefused(session, player, target, badSkill: true) ||
            SiegeFriendRefused(session, player, target) ||
            PeacePlayableRefused(session, player, target))
            return null;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;
        UpdatePvPStatus(player, target, now);

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = targetId;

        int effectPoint = template.EffectPointAt(level);
        ScheduleEffect(player, target, template, level, now, hitMs, (t, at) =>
        {
            if (t.Kind == ZoneEntityKind.Npc)
                ApplyEffectPointHate(player, t, effectPoint, template, request.SkillId);
            RunInstantPayloadEffects(player, t, request.SkillId);
        });
        return null;
    }

    /// <summary>Mobius <c>AbnormalType.TURN_PASSIVE</c> — Veil's slot, the one abnormal Creature.callSkill exempts.</summary>
    static bool IsTurnPassive(string? slot) =>
        !string.IsNullOrEmpty(slot) && slot.Equals("TURN_PASSIVE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mobius <c>Creature.onMagicHitTimer</c> hate for an attackable victim: a negative <c>effectPoint</c> adds
    /// <c>-effectPoint</c> hate, a positive one reduces it, 0 does nothing. Damage skills keep their own
    /// <c>addDamageHate(damage · 100 / (level + 7))</c> in the melee/skill-damage paths.
    /// </summary>
    /// <summary>
    /// Mobius <c>Creature.callSkill</c> after <c>activateSkill</c>: an attackable target is added to
    /// <c>attackByList</c> (except Lure 51 / Temptation 511), but the AI is only notified as attacked
    /// when the skill is negative, has no <c>EffectType.HATE</c> (<c>DeleteHate</c> / <c>DeleteHateOfMe</c>),
    /// and is not <c>AbnormalType.TURN_PASSIVE</c>. Trick (11) carries DeleteHateOfMe, so an idle mob
    /// must not start attacking the caster. A mob already in combat keeps its current target; the
    /// hate-removal payloads still run.
    /// </summary>
    void ApplyEffectPointHate(
        ZoneEntity caster, ZoneEntity target, int effectPoint, SkillDefinition template, int skillId)
    {
        if (target.Kind != ZoneEntityKind.Npc || effectPoint == 0)
            return;
        if (effectPoint < 0)
        {
            if (SuppressesAttackNotify(template, skillId))
                RecordHate(target, caster, hate: -effectPoint);
            else
                AddHate(target, caster, hate: -effectPoint);
        }
        else
            ReduceHate(target, caster, effectPoint);
    }

    const int LureSkillId = 51;
    const int TemptationSkillId = 511;

    /// <summary>
    /// Mobius <c>callSkill</c> skips <c>notifyActionAttacked</c> for EffectType.HATE and TURN_PASSIVE,
    /// and skips <c>attackByList</c> for Lure / Temptation.
    /// </summary>
    bool SuppressesAttackNotify(SkillDefinition template, int skillId)
    {
        if (skillId is LureSkillId or TemptationSkillId)
            return true;
        if (IsTurnPassive(template.AbnormalType))
            return true;
        return HasHateEffectType(skillId);
    }

    /// <summary>Mobius <c>EffectType.HATE</c>: <c>DeleteHate</c> and <c>DeleteHateOfMe</c> (Trick, Bluff, Forget).</summary>
    bool HasHateEffectType(int skillId)
    {
        foreach (var effect in EffectsOf(skillId))
        {
            if (effect.Name.Equals("DeleteHate", StringComparison.OrdinalIgnoreCase) ||
                effect.Name.Equals("DeleteHateOfMe", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mobius <c>EffectFlag.PASSIVE</c> / <c>AbnormalType.TURN_PASSIVE</c>: the victim stops being aggressive and
    /// stops attacking the caster. Its hate entry for the caster is dropped so that, exactly like retail, the mob does
    /// not retaliate for the Veil itself; other attackers stay on the hate list.
    /// </summary>
    void PacifyNpc(ZoneEntity npc, ZoneEntity caster, long now)
    {
        if (npc.Kind != ZoneEntityKind.Npc)
            return;
        if (npc.AggroTargetId > 0)
        {
            _meleeHits.RemoveAll(h => h.AttackerId == npc.ObjectId);
            npc.NextMobAttackMs = now + ZoneCombat.HitTimeMs(NpcPAtkSpd(npc));
            npc.Chasing = false;
            StopTimedMove(npc);
        }
        if (npc.HateList.Remove(caster.ObjectId) && npc.AggroTargetId == caster.ObjectId)
            SetAggroTarget(npc, MostHated(npc));
        BroadcastNpcMove(npc, npc.X, npc.Y, npc.Z);
    }
}

