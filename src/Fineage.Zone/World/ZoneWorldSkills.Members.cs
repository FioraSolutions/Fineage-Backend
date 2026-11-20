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
        // Mobius Player.useMagic: an offensive skill on a player that is not auto-attackable needs Ctrl.
        if (!request.CtrlPressed && NeedsForce(player, target))
            return null;
        if (HostilePlayableRefused(session, player, target, badSkill: true) ||
            SiegeFriendRefused(session, player, target) ||
            PeacePlayableRefused(session, player, target))
            return null;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;

        if (!TryEnsureHostileSkillRange(session, player, target, request, template, now, out var rangeError))
            return rangeError;

        player.Intent.AutoAttackTargetId = 0;
        // Mobius Creature.doCast / Skill.checkCondition: target out of sight → SysMsg 181, no cast.
        if (!CanSeeTarget(player, target))
        {
            CannotSeeTarget(session);
            return null;
        }
        if (_skillCombat.TryGet(request.SkillId, out var combat))
            return CastMobiusAttack(session, player, target, request.SkillId, level, combat, template, now);

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
        if (!InSkillCastRange(player, target, template.CastRange))
            return SkillProtocol.Range;

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        FlagForcedSupport(player, target, request.CtrlPressed, now);
        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        // Mobius onMagicFinalizer: a friendly buff (Dash, Might, Wind Walk) is not isBad() → no attack stance.
        EnterCombatStanceOnCast(player, template, level, now);
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        // Mobius onMagicHitTimer: the buff lands at hitTime (interruptible for magic/static skills).
        ScheduleEffect(player, target, template, level, now, hitMs,
            (t, at) => ApplyAbnormalFromSkill(template, levelData, t, at, attacker: player));
        return null;
    }


    /// <summary>
    /// Mobius <c>isInsideRadius2D(target, castRange + both collision radii)</c>. Used for friendly skills
    /// that simply refuse when the target is too far (no moveToPawn).
    /// </summary>
    bool InSkillCastRange(ZoneEntity caster, ZoneEntity target, int castRange) =>
        ZoneCombat.Within(caster.X, caster.Y, target.X, target.Y, SkillCastReach(castRange, caster, target));

    /// <summary>
    /// Mobius <c>AI_INTENTION_CAST</c>: if the target is outside <see cref="SkillCastReach"/>, walk in
    /// (<c>maybeMoveToPawn(target, castRange)</c>) and queue the cast on arrival. The +100 in Mobius is only
    /// follow hysteresis, not extra start range.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the caster is already in range and the skill may start.
    /// <see langword="false"/> when an approach was started (<paramref name="error"/> is null) or the target
    /// cannot be reached (<paramref name="error"/> is <see cref="SkillProtocol.Range"/>).
    /// </returns>
    bool TryEnsureHostileSkillRange(
        ZoneSession session,
        ZoneEntity player,
        ZoneEntity target,
        SkillUseIntent request,
        Fineage.Game.Skills.SkillDefinition template,
        long now,
        out string? error)
    {
        error = null;
        int reach = SkillCastReach(template.CastRange, player, target);
        if (ZoneCombat.Within(player.X, player.Y, target.X, target.Y, reach))
            return true;
        long travelMs = ApproachTarget(player, target, reach);
        if (travelMs < 0)
        {
            error = SkillProtocol.Range;
            return false;
        }
        player.Intent.AutoAttackTargetId = 0;
        player.Intent.PendingTalkNpcId = 0;
        player.Intent.PendingTalkAtUnixMs = 0;
        session.SelectedTargetId = target.ObjectId;
        player.Intent.PendingCastSkillId = request.SkillId;
        player.Intent.PendingCastTargetId = target.ObjectId;
        player.Intent.PendingCastAtUnixMs = now + travelMs;
        return false;
    }

    /// <summary>Instant heal/utility (Elemental Heal, Heal, Self Heal, …).</summary>
}
