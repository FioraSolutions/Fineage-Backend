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

    /// <summary>Mobius AREA affectRange around the primary target. Falls back to 200 when unset.</summary>
    static int ResolveAffectRange(SkillDefinition template)
    {
        // skills.json currently stores Mobius effectRange (escape) not affectRange; Poisonous Cloud
        // affectRange is 200. Prefer a sane AoE radius over the 1000 escape value.
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
            if (!ZoneCombat.InCastRange(center.X, center.Y, entity.X, entity.Y, affectRange))
                continue;
            if (entity.ObjectId != center.ObjectId &&
                entity.ObjectId != caster.ObjectId &&
                !VisibleTo(caster.ObjectId, entity.ObjectId))
                continue;
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
            int removed = ApplyDispelByCategory(t, slot, rate, max, cancelMl);
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
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = targetId;
        int power = Math.Max(1, levelData.Power);
        ScheduleEffect(player, target, template, level, now, hitMs, (t, _) =>
        {
            int burned = Math.Min(t.CurMp, power);
            if (burned <= 0)
                return;
            t.CurMp = Math.Max(0, t.CurMp - burned);
            FanoutVitals(t, new StatusUpdateMsg
            {
                ObjectId = t.ObjectId,
                AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
                Values = [t.CurMp, t.MaxMp]
            });
            if (t.Kind == ZoneEntityKind.Npc)
                AddHate(t, player, hate: Math.Max(1, burned));
        });
        return null;
    }

    /// <summary>Mobius calcCancelEffects for buff/debuff categories on a live target.</summary>
    int ApplyDispelByCategory(ZoneEntity target, string slot, int rate, int max, int cancelMagicLevel)
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
                return ZoneCombat.RollCancelSuccess(_combatRandom, rate, cancelMagicLevel, buffMl);
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
    /// Ctrl-forced heal/buff on a non-self target raises the caster's PvP flag (purple name), including
    /// when the target is a mob — Interlude force-use support behaviour the playtest expects.
    /// </summary>
    void FlagForcedSupport(ZoneEntity caster, ZoneEntity target, bool ctrlPressed, long now)
    {
        if (!ctrlPressed || ReferenceEquals(caster, target))
            return;
        if (target.Kind == ZoneEntityKind.Player)
            UpdatePvPStatus(caster, target, now);
        else
            RaisePvpFlag(caster, now, PvpNormalTimeMs);
    }

    void IncorrectTarget(ZoneSession session) =>
        ShotNotice(session, SysMsgIncorrectTarget, "That is an incorrect target.");

    /// <summary>Mobius calcAtkSpd for skill HitTime / ReuseDelay (magic ·333/Cspd, phys ·300/Pspd for hit).</summary>
    (int HitMs, int ReuseMs) ScaledSkillTimes(ZoneEntity player, SkillDefinition template)
    {
        var ps = PlayerStats(player);
        bool magic = template.MagicType == 1;
        return (
            ZoneCombat.SkillTimeMs(template.HitTimeMs, magic, ps.MAtkSpd, ps.PAtkSpd),
            ZoneCombat.SkillReuseMs(template.ReuseDelayMs, magic, ps.MAtkSpd, ps.PAtkSpd));
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
