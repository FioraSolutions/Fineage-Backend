using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public const int SysMsgUnsuitableTerms = 113;

    string? TrySkillUseAttack(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        // Mobius Skill.checkCondition: <using kind=..> fails → SysMsg 113 "<skill> cannot be used due to unsuitable terms."
        if (_skillCombat.TryGet(request.SkillId, out var cond) && cond.UsingKinds.Length > 0 && !Wearing(player, cond.UsingKinds))
        {
            ShotNotice(session, SysMsgUnsuitableTerms, $"{cond.Name} cannot be used due to unsuitable terms.");
            return null;
        }
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

        bool inRange = template.CastRange > 0
            ? ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange + 100)
            : ZoneCombat.InMeleeRange(player.X, player.Y, target.X, target.Y);
        if (!inRange)
        {
            // Mobius AI_INTENTION_CAST: moveToPawn(target, castRange) then cast on arrival instead of refusing.
            int reach = template.CastRange > 0 ? Math.Max(40, template.CastRange) : ZoneCombat.MeleeRange;
            long travelMs = ApproachTarget(player, target, reach);
            if (travelMs < 0)
                return SkillProtocol.Range;
            session.AutoAttackTargetId = 0;
            session.PendingTalkNpcId = 0;
            session.PendingTalkAtUnixMs = 0;
            session.SelectedTargetId = targetId;
            session.PendingCastSkillId = request.SkillId;
            session.PendingCastTargetId = targetId;
            session.PendingCastAtUnixMs = now + travelMs;
            return null;
        }

        session.AutoAttackTargetId = 0;
        // Mobius Creature.doCast / Skill.checkCondition: target out of sight → SysMsg 181, no cast.
        if (!CanSeeTarget(player, target))
        {
            CannotSeeTarget(session);
            return null;
        }
        if (_skillCombat.TryGet(request.SkillId, out var combat))
            return CastMobiusAttack(session, player, target, request.SkillId, level, combat, now);

        // Fail-loud: attack skills without skill-combat.json rows must not invent damage (was power+10 stub).
        ZoneLog.Warn($"skill-combat missing skillId={request.SkillId} — refuse cast");
        return SkillProtocol.NoCombat;
    }

    string? TrySkillUseBuff(
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
        if (!IsFriendlySkillTarget(player, target, request.CtrlPressed) || target.Dead || target.CurHp <= 0)
        {
            IncorrectTarget(session);
            return null;
        }
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
        {
            IncorrectTarget(session);
            return null;
        }
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        FlagForcedSupport(player, target, request.CtrlPressed, now);
        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        // Mobius onMagicHitTimer: the buff lands at hitTime (interruptible for magic/static skills).
        ScheduleEffect(player, target, template, level, now, hitMs,
            (t, at) => ApplyAbnormalFromSkill(template, levelData, t, at, attacker: player));
        return null;
    }

    /// <summary>Instant heal/utility (Elemental Heal, Heal, Self Heal, …).</summary>
}
