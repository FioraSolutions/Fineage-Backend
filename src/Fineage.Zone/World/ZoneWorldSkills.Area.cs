using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>affectRange</c>: stored on the template when present, else 200 for AURA/AREA.</summary>
    static int ResolveAffectRange(SkillDefinition template)
    {
        if (template.AffectRange > 0)
            return template.AffectRange;
        // skills.json historically stored Mobius effectRange (escape) on some AREA skills (Poisonous Cloud 200).
        if (template.EffectRange > 0 && template.EffectRange <= 400)
            return template.EffectRange;
        return 200;
    }

    void ApplyAreaDebuff(
        ZoneEntity caster,
        ZoneEntity center,
        SkillDefinition template,
        SkillLevelData levelData,
        long nowUnixMs,
        int affectRange)
    {
        if (center.Dead || center.CurHp <= 0)
            return;
        int hate = Math.Max(1, Math.Abs(levelData.Power));
        foreach (var entity in _byId.Values)
        {
            if (entity.Dead || entity.CurHp <= 0)
                continue;
            if (!IsAttackable(caster, entity))
                continue;
            // Mobius area offensive skills skip players that are not auto-attackable (white, party mates) unless
            // they are the target the caster picked; peace zones protect splash victims too.
            if (entity.Kind == ZoneEntityKind.Player && entity.ObjectId != center.ObjectId &&
                (!PlayerAutoAttackable(caster, entity) || (caster.Session != null &&
                    PeacePlayableRefused(caster.Session, caster, entity, notify: false))))
                continue;
            if (!ZoneCombat.InCastRange(center.X, center.Y, entity.X, entity.Y, affectRange))
                continue;
            if (entity.ObjectId != center.ObjectId &&
                entity.ObjectId != caster.ObjectId &&
                !VisibleTo(caster.ObjectId, entity.ObjectId))
                continue;
            if (entity.Kind == ZoneEntityKind.Player)
                UpdatePvPStatus(caster, entity, nowUnixMs);
            if (!ApplyAbnormalFromSkill(template, levelData, entity, nowUnixMs, attacker: caster))
                continue;
            if (entity.Kind == ZoneEntityKind.Npc)
                AddHate(entity, caster, hate: hate);
        }
    }

    /// <summary>
    /// Mobius Cancellation / DispelByCategory: hostile cast that strips buffs (not a heal / buff).
    /// </summary>
    string? TrySkillUseCancel(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now,
        SkillEffectCatalog.Effect dispel)
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
        if (!TryEnsureHostileSkillRange(session, player, target, request, template, now, out var rangeError))
            return rangeError;
        // Mobius callSkill: a negative skill on a playable flags the caster (landing or resisted).
        UpdatePvPStatus(player, target, now);

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        // Mobius onMagicFinalizer: Cancellation is isBad() (negative effectPoint, non-SELF) → attack stance.
        EnterCombatStanceOnCast(player, template, level, now);
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = targetId;
        if (levelData.MagicLevel <= 0)
            throw new InvalidOperationException(
                $"skill {request.SkillId} lv{levelData.Level} missing magicLevel for calcCancelSuccess");
        int cancelMl = levelData.MagicLevel;
        if (string.IsNullOrWhiteSpace(dispel.Slot))
            throw new InvalidOperationException($"skill {request.SkillId} DispelByCategory missing slot");
        if (dispel.Rate <= 0)
            throw new InvalidOperationException($"skill {request.SkillId} DispelByCategory missing rate");
        if (dispel.Max <= 0)
            throw new InvalidOperationException($"skill {request.SkillId} DispelByCategory missing max");
        string slot = dispel.Slot;
        int rate = dispel.Rate;
        int max = dispel.Max;
        ScheduleEffect(player, target, template, level, now, hitMs, (t, _) =>
        {
            int removed = ApplyDispelByCategory(player, t, slot, rate, max, cancelMl);
            if (removed > 0)
                FanoutAbnormal(t);
            if (t.Kind == ZoneEntityKind.Npc)
                AddHate(t, player, hate: Math.Max(1, removed * 10));
        });
        return null;
    }

    /// <summary>Mobius MagicalAttackMp (Mana Burn): burns target MP, not HP.</summary>
    string? TrySkillUseManaBurn(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        ZoneEntity? primary = null;
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (IsMultiHostileTarget(template))
        {
            if (targetId > 0 && _byId.TryGetValue(targetId, out var selected) &&
                IsAttackable(player, selected))
                primary = selected;
        }
        else
        {
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
            if (!TryEnsureHostileSkillRange(session, player, target, request, template, now, out var rangeError))
                return rangeError;
            primary = target;
        }

        if (primary != null)
            UpdatePvPStatus(player, primary, now);

        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var anim = primary ?? player;
        var cast = BuildCast(player, anim, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, anim, now);
        FanoutSkill(player, anim, cast, targetStatus: null, CasterMpStatus(player));
        EnterCombatStanceOnCast(player, template, level, now);
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = primary?.ObjectId ?? session.SelectedTargetId;
        int power = Math.Max(1, levelData.Power);
        var victims = IsMultiHostileTarget(template)
            ? HostileAffectTargets(player, primary, template).ToList()
            : [primary!];
        ScheduleEffect(player, anim, template, level, now, hitMs, (_, at) =>
        {
            foreach (var t in victims)
            {
                if (t.Dead || t.CurMp <= 0)
                    continue;
                if (t.Kind == ZoneEntityKind.Player &&
                    (SiegeFriendRefused(session, player, t) ||
                     PeacePlayableRefused(session, player, t, notify: false)))
                    continue;
                int burned = Math.Min(t.CurMp, power);
                if (burned <= 0)
                    continue;
                t.CurMp = Math.Max(0, t.CurMp - burned);
                FanoutVitals(t, new StatusUpdateMsg
                {
                    ObjectId = t.ObjectId,
                    AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
                    Values = [t.CurMp, t.MaxMp]
                });
                if (t.Kind == ZoneEntityKind.Npc)
                    AddHate(t, player, hate: Math.Max(1, burned));
            }
        });
        return null;
    }

    /// <summary>Mobius calcCancelEffects for buff/debuff categories on a live target.</summary>
    int ApplyDispelByCategory(ZoneEntity caster, ZoneEntity target, string slot, int rate, int max, int cancelMagicLevel)
    {
        bool wantBuff = slot.Equals("buff", StringComparison.OrdinalIgnoreCase);
        return ZoneAbnormals.DispelByCategory(
            target.Abnormals,
            entry =>
            {
                var skill = _game.Skills.Find(entry.Id);
                if (skill == null)
                    return wantBuff; // unknown → treat as buff strip candidate
                return wantBuff ? !skill.IsDebuff : skill.IsDebuff;
            },
            entry =>
            {
                var skill = _game.Skills.Find(entry.Id);
                int buffMl = 0;
                if (skill != null && skill.TryLevelData(Math.Max(1, entry.SkillLevel), out var ld))
                    buffMl = ld.MagicLevel;
                return ZoneCombat.RollCancelSuccess(Rng(_combatRandom, caster), rate, cancelMagicLevel, buffMl);
            },
            max);
    }

    /// <summary>Mobius SystemMessageId 144 THAT_IS_AN_INCORRECT_TARGET.</summary>
    public const int SysMsgIncorrectTarget = 144;

    /// <summary>
    /// Friendly targeting for support skills.
    /// SELF → caster. Explicit living player → that player. Explicit NPC/monster with Ctrl (forceUse)
    /// → that creature. Without Ctrl, a non-player selection is invalid (no silent self-redirect).
    /// Empty selection → caster (self cast with no target).
    /// </summary>
    int ResolveFriendlyTargetId(ZoneSession session, ZoneEntity player, SkillUseIntent request, SkillDefinition template)
    {
        if (string.Equals(template.TargetType, "SELF", StringComparison.OrdinalIgnoreCase))
            return player.ObjectId;

        int requested = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (requested <= 0)
            return player.ObjectId;

        if (!_byId.TryGetValue(requested, out var candidate) || candidate.Dead)
            return 0;

        if (candidate.Kind == ZoneEntityKind.Player)
            return requested;

        // Mobius: positive-effect skills on a monster require forceUse (ctrl); else ActionFailed / invalid.
        return request.CtrlPressed ? requested : 0;
    }

    /// <summary>
    /// Living player always; living NPC only when Ctrl forced the support cast.
    /// </summary>
    static bool IsFriendlySkillTarget(ZoneEntity caster, ZoneEntity target, bool ctrlPressed)
    {
        if (ReferenceEquals(caster, target))
            return true;
        return target.Kind switch
        {
            ZoneEntityKind.Player => true,
            ZoneEntityKind.Npc => ctrlPressed,
            _ => false
        };
    }

    /// <summary>
    /// Mobius <c>Creature.callSkill</c> for a positive skill: helping a flagged or karma player, or any attackable
    /// creature (Ctrl-forced buff on a mob), runs <c>updatePvPStatus()</c> (normal flag time, not in PVP zones).
    /// Healing or buffing a white player never flags.
    /// </summary>
    void FlagForcedSupport(ZoneEntity caster, ZoneEntity target, bool ctrlPressed, long now)
    {
        if (caster.Kind != ZoneEntityKind.Player || ReferenceEquals(caster, target) || InPvpArea(caster))
            return;
        bool flags = target.Kind == ZoneEntityKind.Player
            ? target.PvpFlagged || target.Karma > 0
            : ctrlPressed;
        if (flags)
            RaisePvpFlag(caster, now, PvpNormalTimeMs);
    }

    void IncorrectTarget(ZoneSession session) =>
        ShotNotice(session, SysMsgIncorrectTarget, "That is an incorrect target.");

    /// <summary>
    /// Mobius <c>Skill.isBad()</c>: <c>effectPoint &lt; 0 &amp;&amp; targetType != SELF</c>. Only a "bad" cast puts the caster
    /// into the attack stance — <c>Creature.onMagicFinalizer</c> runs <c>clientStartAutoAttack()</c> under exactly this
    /// condition, and <c>AttackStanceTaskManager</c> then holds <c>atkwait</c> for 15 s.
    /// <para>
    /// A self/party buff is not bad: Dash (4) carries <c>effectPoint +204/+438</c> with <c>targetType SELF</c>, Heal
    /// (1011) and the cleanses are positive as well. Writing <c>CombatUntilUnixMs</c> for those raised the weapon
    /// stance after a Dash, and the same window blocks summon / private store / clan actions for 15 s.
    /// </para>
    /// </summary>
    static bool IsBadSkillForStance(SkillDefinition template, int level) =>
        template.EffectPointAt(level) < 0 &&
        !string.Equals(template.TargetType, "SELF", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stance after a cast: Mobius <c>onMagicFinalizer</c> keeps the 15 s attack stance only for an <see
    /// cref="IsBadSkillForStance">offensive</see> skill. Anything else leaves whatever window was already running.</summary>
    void EnterCombatStanceOnCast(ZoneEntity player, SkillDefinition template, int level, long now)
    {
        if (IsBadSkillForStance(template, level))
            player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
    }

    /// <summary>Mobius calcAtkSpd for skill HitTime / ReuseDelay (magic ·333/Cspd, phys ·300/Pspd for hit).</summary>
    (int HitMs, int ReuseMs) ScaledSkillTimes(ZoneEntity player, SkillDefinition template, int level = 1)
    {
        var ps = PlayerStats(player);
        bool magic = template.MagicType == 1;
        return (
            ZoneCombat.SkillTimeMs(template.HitTimeMs, magic, ps.MAtkSpd, ps.PAtkSpd),
            SkillReuseMsFor(player, template, template.ReuseDelayMsAt(level)));
    }

    static MagicSkillUseMsg BuildCast(
        ZoneEntity caster,
        ZoneEntity target,
        int skillId,
        int level,
        int hitTimeMs,
        int reuseDelayMs) => new()
        {
            CasterObjectId = caster.ObjectId,
            TargetObjectId = target.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = hitTimeMs,
            ReuseDelayMs = reuseDelayMs
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
        var netWatchers = ActorAudience(caster, target);
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
