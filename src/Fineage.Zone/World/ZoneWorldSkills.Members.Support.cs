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
        if (HostilePlayableRefused(session, player, target, badSkill: true) ||
            PeacePlayableRefused(session, player, target))
            return null;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

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
        // Debuffs also add hate (Mobius EffectPoint / addDamageHate) so Fear AI and retaliation start
        // even when the cast dealt no HP damage.
        // AREA (Poisonous Cloud): affect around the primary target (Mobius affectRange, typically 200).
        bool isArea = template.TargetType.Equals("AREA", StringComparison.OrdinalIgnoreCase);
        int affectRange = isArea ? ResolveAffectRange(template) : 0;
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
                AddHate(t, player, hate: Math.Max(1, Math.Abs(levelData.Power)));
        });
        return null;
    }
}

