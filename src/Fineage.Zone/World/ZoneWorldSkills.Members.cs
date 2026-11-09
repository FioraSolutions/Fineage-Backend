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
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Player || target.Dead || target.CurHp <= 0)
            return SkillProtocol.Target;
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, template.HitTimeMs);
        // Mobius onMagicHitTimer: the buff lands at hitTime (interruptible for magic/static skills).
        ScheduleEffect(player, target, template, level, now, template.HitTimeMs,
            (t, at) => ApplyAbnormalFromSkill(template, levelData, t, at));
        return null;
    }

    /// <summary>
    /// Instant heal/utility (Elemental Heal, Heal, Self Heal, …). Datapack heal power is often 0 —
    /// use a Mobius-ish fallback so SELF heals work with a mob selected.
    /// </summary>
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
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Player || target.Dead)
            return SkillProtocol.Target;
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (template.CastRange > 0 &&
            targetId != player.ObjectId &&
            !ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        int heal = SupportHealAmount(levelData, level, player.Level);
        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, template.HitTimeMs);
        // Mobius Heal effect at hitTime: HP rises when the cast completes, not when it starts.
        ScheduleEffect(player, target, template, level, now, template.HitTimeMs, (t, _) =>
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

    static int SupportHealAmount(SkillLevelData levelData, int skillLevel, int playerLevel)
    {
        if (levelData.Power > 0)
            return levelData.Power;
        // Datapack omitted heal power; scale by skill level and character level until import is fixed.
        return Math.Max(25, 18 + skillLevel * 10 + Math.Max(1, playerLevel) * 4);
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
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, template.HitTimeMs);
        session.SelectedTargetId = targetId;
        // Mobius onMagicHitTimer: debuff lands at hitTime; a dead/escaped/hidden target aborts it.
        ScheduleEffect(player, target, template, level, now, template.HitTimeMs,
            (t, at) => ApplyAbnormalFromSkill(template, levelData, t, at));
        return null;
    }

    /// <summary>
    /// Friendly skills: SELF always self. Otherwise prefer an explicit player target; if the client
    /// sent a mob (selected target), fall back to self so Heal works without clearing the target.
    /// </summary>
    int ResolveFriendlyTargetId(ZoneSession session, ZoneEntity player, SkillUseIntent request, SkillDefinition template)
    {
        if (string.Equals(template.TargetType, "SELF", StringComparison.OrdinalIgnoreCase))
            return player.ObjectId;

        int requested = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (requested > 0 &&
            _byId.TryGetValue(requested, out var candidate) &&
            candidate.Kind == ZoneEntityKind.Player &&
            !candidate.Dead)
            return requested;

        return player.ObjectId;
    }

    static MagicSkillUseMsg BuildCast(
        ZoneEntity caster,
        ZoneEntity target,
        int skillId,
        int level,
        SkillDefinition template) => new()
        {
            CasterObjectId = caster.ObjectId,
            TargetObjectId = target.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = template.HitTimeMs,
            ReuseDelayMs = template.ReuseDelayMs
        };

    static StatusUpdateMsg CasterMpStatus(ZoneEntity player) => new()
    {
        ObjectId = player.ObjectId,
        AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
        Values = [player.CurMp, player.MaxMp]
    };

    void FanoutSkill(
        ZoneEntity caster,
        ZoneEntity target,
        MagicSkillUseMsg cast,
        StatusUpdateMsg? targetStatus,
        StatusUpdateMsg casterMp)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, caster) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(caster.ObjectId);
            if (!cares)
                continue;

            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutMagicSkillUse(netWatchers, cast);
            if (targetStatus != null)
            {
                NetBridge.FanoutStatusUpdate(netWatchers, targetStatus);
            }

            NetBridge.FanoutStatusUpdate([caster.ObjectId], casterMp);
        }
    }
}
