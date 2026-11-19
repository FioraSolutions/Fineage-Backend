using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius instant effect classes (GetAgro, TargetMe, HpDrain, HpByLevel, CpDamPercent, Lethal, RunAway,
/// PolearmSingleTarget, ConsumeBody). Classification is by effect name, never by skill id.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>SystemMessageId.S1_HP_HAS_BEEN_RESTORED</c> (322).</summary>
    public const int SysMsgHpRestored = 322;

    static bool EffectNameIs(SkillEffectCatalog.Effect effect, string name) =>
        effect.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    static bool RowsHave(SkillEffectCatalog.Effect[] rows, string name)
    {
        foreach (var row in rows)
        {
            if (EffectNameIs(row, name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Instant hostile payloads that must not fall through to <c>skill-combat.json</c> damage:
    /// GetAgro / TargetMe, CpDamPercent, Lethal, RunAway.
    /// </summary>
    static bool HasHostileInstantPayload(SkillEffectCatalog.Effect[] rows) =>
        RowsHave(rows, "GetAgro") || RowsHave(rows, "TargetMe") ||
        RowsHave(rows, "CpDamPercent") || RowsHave(rows, "Lethal") || RowsHave(rows, "RunAway");

    /// <summary>Mobius Life Scavenge family: <c>HpByLevel</c> on a <c>CORPSE_MOB</c> plus <c>ConsumeBody</c>.</summary>
    static bool HasCorpseScavengePayload(SkillEffectCatalog.Effect[] rows) =>
        RowsHave(rows, "HpByLevel");

    internal bool HasPolearmSingleTarget(ZoneEntity player)
    {
        foreach (var ab in player.Abnormals)
        {
            if (_skillEffects.TryGet(ab.Id, out var rows) && RowsHave(rows, "PolearmSingleTarget"))
                return true;
        }
        return false;
    }

    bool EffectChanceFails(SkillEffectCatalog.Effect effect)
    {
        int chance = effect.Chance;
        // Missing chance (0) means Mobius default 100 — the roll is skipped.
        if (chance <= 0 || chance >= 100)
            return false;
        return _combatRandom.Next(100) >= chance;
    }

    static bool IsAuraTarget(SkillDefinition template) =>
        template.TargetType.Equals("AURA", StringComparison.OrdinalIgnoreCase);

    static bool IsFrontAreaTarget(SkillDefinition template) =>
        template.TargetType.Equals("FRONT_AREA", StringComparison.OrdinalIgnoreCase);

    static bool IsAreaTarget(SkillDefinition template) =>
        template.TargetType.Equals("AREA", StringComparison.OrdinalIgnoreCase);

    static bool IsMultiHostileTarget(SkillDefinition template) =>
        IsAuraTarget(template) || IsFrontAreaTarget(template) || IsAreaTarget(template);

    /// <summary>
    /// Mobius AURA (around the caster), AREA (around the primary target) and FRONT_AREA (in front of the caster).
    /// </summary>
    IEnumerable<ZoneEntity> HostileAffectTargets(ZoneEntity caster, ZoneEntity? primary, SkillDefinition template)
    {
        int range = ResolveAffectRange(template);
        int cx, cy;
        if (IsAuraTarget(template) || IsFrontAreaTarget(template) || primary == null)
        {
            cx = caster.X;
            cy = caster.Y;
        }
        else
        {
            cx = primary.X;
            cy = primary.Y;
        }

        foreach (var entity in _byId.Values)
        {
            if (entity.Dead || entity.CurHp <= 0)
                continue;
            if (ReferenceEquals(entity, caster))
                continue;
            if (!IsAttackable(caster, entity))
                continue;
            if (entity.Kind == ZoneEntityKind.Player &&
                (primary == null || entity.ObjectId != primary.ObjectId) &&
                (!PlayerAutoAttackable(caster, entity) || (caster.Session != null &&
                    PeacePlayableRefused(caster.Session, caster, entity, notify: false))))
                continue;
            if (!ZoneCombat.InCastRange(cx, cy, entity.X, entity.Y, range))
                continue;
            if (IsFrontAreaTarget(template) &&
                !ZoneCombat.IsInFrontOf(entity.X, entity.Y, caster.X, caster.Y, caster.Heading))
                continue;
            if (entity.ObjectId != caster.ObjectId && !VisibleTo(caster.ObjectId, entity.ObjectId))
                continue;
            yield return entity;
        }
    }

    /// <summary>
    /// Apply abnormals (when the skill has one), effectPoint hate, and instant payloads to every affect target.
    /// </summary>
    void ApplyHostileAffect(
        ZoneEntity caster, ZoneEntity? primary, SkillDefinition template, SkillLevelData levelData, int level, long now)
    {
        if (primary != null && (primary.Dead || primary.CurHp <= 0) && !IsMultiHostileTarget(template))
            return;
        int effectPoint = template.EffectPointAt(level);
        var targets = IsMultiHostileTarget(template)
            ? HostileAffectTargets(caster, primary, template).ToList()
            : (primary != null ? new List<ZoneEntity> { primary } : new List<ZoneEntity>());
        foreach (var t in targets)
        {
            if (t.Kind == ZoneEntityKind.Player)
                UpdatePvPStatus(caster, t, now);
            if (template.AppliesAbnormal)
                ApplyAbnormalFromSkill(template, levelData, t, now, attacker: caster);
            if (t.Kind == ZoneEntityKind.Npc)
                ApplyEffectPointHate(caster, t, effectPoint, template, template.SkillId);
            if (IsTurnPassive(template.AbnormalType))
                PacifyNpc(t, caster, now);
            RunInstantPayloadEffects(caster, t, template.SkillId, level, now);
        }
    }

    /// <summary>
    /// Mobius GetAgro: chance-gated, set attack intention on an attackable; clan-mates in <c>clanHelpRange</c>
    /// gain 200 hate. Optional <c>affectPlayers</c> starts auto-attack on the victim (and nearby enemies in <c>range</c>).
    /// </summary>
    internal void ApplyGetAgro(ZoneEntity caster, ZoneEntity target, SkillEffectCatalog.Effect effect, long now)
    {
        if (EffectChanceFails(effect))
            return;
        if (target.Kind == ZoneEntityKind.Npc && target.Attackable)
        {
            if (target.CurHp <= 0)
                return;
            AddHate(target, caster, hate: 1);
            if (target.Running == false)
            {
                target.Running = true;
                FanoutMoveType(target);
            }
            CallGetAgroClanAssist(target, caster, now);
            return;
        }

        bool affectPlayers = effect.ReadExtra("affectPlayers").Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!affectPlayers || target.Kind != ZoneEntityKind.Player)
            return;
        StartAutoAttackOn(target, caster);
        int range = effect.ReadExtraInt("range");
        if (range <= 0 || caster.Kind != ZoneEntityKind.Player)
            return;
        foreach (var nearby in Players())
        {
            if (nearby.ObjectId == caster.ObjectId || nearby.ObjectId == target.ObjectId || nearby.Dead)
                continue;
            if (!ZoneCombat.InCastRange(target.X, target.Y, nearby.X, nearby.Y, range))
                continue;
            if (!IsAttackable(caster, nearby) || !PlayerAutoAttackable(nearby, caster))
                continue;
            // Same party / clan do not turn on the caster (Mobius GetAgro).
            if (SamePartyOrClan(nearby, caster))
                continue;
            StartAutoAttackOn(nearby, caster);
        }
    }

    void CallGetAgroClanAssist(ZoneEntity targetNpc, ZoneEntity caster, long now)
    {
        if (targetNpc.Clans == null || targetNpc.ClanHelpRange <= 0)
            return;
        foreach (int id in _aoi.GetVisible(targetNpc.ObjectId))
        {
            if (!_byId.TryGetValue(id, out var other))
                continue;
            if (other.Kind != ZoneEntityKind.Npc || ReferenceEquals(other, targetNpc) || other.CurHp <= 0 ||
                !other.Attackable || other.Clans == null || !other.Clans.Any(c => targetNpc.Clans.Contains(c)) ||
                !ZoneCombat.Within(targetNpc.X, targetNpc.Y, other.X, other.Y, targetNpc.ClanHelpRange) ||
                !SameInstance(targetNpc.ObjectId, other.ObjectId))
                continue;
            AddHate(other, caster, hate: 200);
            if (!other.Running)
            {
                other.Running = true;
                FanoutMoveType(other);
            }
        }
    }

    /// <summary>Mobius TargetMe: lock an attackable (or playable, when PvP allows) onto the caster.</summary>
    internal void ApplyTargetMe(ZoneEntity caster, ZoneEntity target, SkillEffectCatalog.Effect effect)
    {
        if (EffectChanceFails(effect))
            return;
        if (target.Kind == ZoneEntityKind.Npc && target.Attackable && target.CurHp > 0)
        {
            AddHate(target, caster, hate: 1);
            return;
        }
        if (target.Kind == ZoneEntityKind.Player)
            StartAutoAttackOn(target, caster);
    }

    void StartAutoAttackOn(ZoneEntity attacker, ZoneEntity victim)
    {
        if (attacker.Session == null || victim.Dead)
            return;
        attacker.Session.SelectedTargetId = victim.ObjectId;
        attacker.Session.AutoAttackTargetId = victim.ObjectId;
    }

    bool SamePartyOrClan(ZoneEntity a, ZoneEntity b) =>
        SameParty(a.ObjectId, b.ObjectId) || (a.ClanId != 0 && a.ClanId == b.ClanId);

    /// <summary>Mobius CpDamPercent: <c>(currentCp · power) / 100</c>, players only. Wakes Fake Death.</summary>
    internal void ApplyCpDamPercent(ZoneEntity caster, ZoneEntity target, SkillEffectCatalog.Effect effect, int level)
    {
        if (target.Kind != ZoneEntityKind.Player)
            return;
        if (target.FakeDeath)
            WakeUpFromFakeDeath(target, "damage");
        double power = effect.PowerAt(level);
        int damage = (int)(target.CurCp * power / 100d);
        if (damage <= 0)
            return;
        target.CurCp = Math.Max(0, target.CurCp - damage);
        FanoutVitals(target, PlayerVitalsStatus(target));
        MaybeBreakCast(target, damage);
    }

    /// <summary>Mobius Lethal effect: full → HP (and player CP) to 1; half → monster HP ×0.5 or player CP to 1.</summary>
    internal void ApplyLethalEffect(ZoneEntity caster, ZoneEntity target, SkillEffectCatalog.Effect effect, SkillDefinition? template, int level)
    {
        int full = effect.ReadExtraInt("fullLethal");
        int half = effect.ReadExtraInt("halfLethal");
        int magicLevel = 0;
        if (template != null && template.TryLevelData(level, out var ld))
            magicLevel = ld.MagicLevel;
        else if (template != null)
            magicLevel = caster.Level;
        bool lethalable = target.Kind != ZoneEntityKind.Npc || target.NpcType is not ("RaidBoss" or "GrandBoss");
        int hp = Math.Max(0, target.CurHp);
        var (extra, outcome) = ZoneCombat.RollLethal(_combatRandom, full, half, magicLevel, target.Level, hp, lethalable);
        if (outcome == ZoneCombat.LethalOutcome.None)
            return;
        if (target.Kind == ZoneEntityKind.Player)
        {
            if (outcome == ZoneCombat.LethalOutcome.Full)
            {
                target.CurCp = 1;
                target.CurHp = 1;
            }
            else
                target.CurCp = 1;
            FanoutVitals(target, PlayerVitalsStatus(target));
        }
        else
        {
            if (extra > 0)
                target.CurHp = Math.Max(1, target.CurHp - extra);
            FanoutVitals(target, StatusFor(target));
        }
        if (caster.Session != null)
        {
            if (outcome == ZoneCombat.LethalOutcome.Full)
                ShotNotice(caster.Session, SysMsgLethalSuccess, "Your lethal strike was successful!");
            else
                ShotNotice(caster.Session, 0, "Half-Kill!");
        }
    }

    /// <summary>Mobius RunAway: attackable only, <c>Rnd(100) &gt; power</c> fails, then fear for <c>time</c> seconds.</summary>
    internal void ApplyRunAway(ZoneEntity caster, ZoneEntity target, SkillEffectCatalog.Effect effect, int skillId, long now)
    {
        if (target.Kind != ZoneEntityKind.Npc || !target.Attackable || target.Dead)
            return;
        int power = (int)effect.Power;
        if (power > 0 && _combatRandom.Next(100) > power)
            return;
        MaybeBreakCast(target, 1);
        int timeSec = effect.ReadExtraInt("time");
        if (timeSec <= 0)
            timeSec = 20;
        StartFearFlee(target, caster, alongHeading: false);
        long until = now + timeSec * 1000L;
        target.Abnormals.RemoveAll(a => a.Slot.Equals("TURN_FLEE", StringComparison.OrdinalIgnoreCase));
        target.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = skillId,
            Value = 1,
            SkillLevel = 1,
            Slot = "TURN_FLEE",
            ExpiresAtUnixMs = until
        });
        _withAbnormals.Add(target.ObjectId);
        FanoutAbnormal(target);
    }

    /// <summary>Mobius HpByLevel: heal the effector by the effect power (Life Scavenge <c>#drain</c> table).</summary>
    internal void ApplyHpByLevel(ZoneEntity caster, SkillEffectCatalog.Effect effect, int level)
    {
        int abs = (int)effect.PowerAt(level);
        if (abs <= 0 || caster.MaxHp <= 0)
            return;
        int before = caster.CurHp;
        caster.CurHp = Math.Min(caster.MaxHp, caster.CurHp + abs);
        int restored = caster.CurHp - before;
        if (restored <= 0)
            return;
        if (caster.Kind == ZoneEntityKind.Player)
            FanoutVitals(caster, PlayerVitalsStatus(caster));
        else
            FanoutVitals(caster, StatusFor(caster));
        if (caster.Session != null)
            ShotNotice(caster.Session, SysMsgHpRestored, $"{restored} HP has been restored.");
    }

    /// <summary>
    /// Mobius HpDrain: of the landed magic damage, only the HP portion (after CP) is absorbed, × effect power (0.2).
    /// </summary>
    internal void ApplyHpDrainAfterDamage(ZoneEntity caster, ZoneEntity target, int damage, int skillId)
    {
        if (damage <= 0 || caster.Dead || caster.CurHp <= 0)
            return;
        if (!_skillEffects.TryGet(skillId, out var rows))
            return;
        double ratio = 0;
        foreach (var effect in rows)
        {
            if (EffectNameIs(effect, "HpDrain") && effect.Power > 0)
            {
                ratio = effect.Power;
                break;
            }
        }
        if (ratio <= 0)
            return;
        int hpPortion = damage;
        if (target.Kind == ZoneEntityKind.Player)
        {
            // Damage is applied to HP after CP in some paths; we approximate the Mobius split:
            // drain is 0 when the hit only shaved CP, else damage − cp (already spent) is not recoverable here.
            // Callers pass the HP actually removed when possible; when only the raw roll is known, assume no CP.
            hpPortion = damage;
        }
        int drain = (int)(ratio * hpPortion);
        if (drain <= 0)
            return;
        int room = Math.Max(0, caster.MaxHp - caster.CurHp);
        drain = Math.Min(drain, room);
        if (drain <= 0)
            return;
        caster.CurHp += drain;
        if (caster.Kind == ZoneEntityKind.Player)
            FanoutVitals(caster, PlayerVitalsStatus(caster));
    }

    /// <summary>
    /// Life Scavenge: corpse-mob target, heal the caster from <c>HpByLevel</c>, then <c>ConsumeBody</c>.
    /// </summary>
    string? TrySkillUseLifeScavenge(
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
        if (target.Kind != ZoneEntityKind.Npc || (!target.Dead && target.CurHp > 0))
        {
            IncorrectTarget(session);
            return null;
        }
        if (!VisibleTo(player.ObjectId, targetId))
        {
            IncorrectTarget(session);
            return null;
        }
        if (template.CastRange > 0 && !InSkillCastRange(player, target, template.CastRange))
            return SkillProtocol.Range;
        if (ConsumeItemBlocked(session, player, levelData))
            return null;

        ConsumeSkillMp(player, template, levelData.MpConsume);
        if (!ConsumeSkillItems(session, player, levelData))
            return null;
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = targetId;

        ScheduleEffect(player, target, template, level, now, hitMs, (t, at) =>
        {
            if (!_byId.ContainsKey(t.ObjectId))
                return;
            if (_skillEffects.TryGet(request.SkillId, out var rows))
            {
                foreach (var effect in rows)
                {
                    if (EffectNameIs(effect, "HpByLevel"))
                        ApplyHpByLevel(player, effect, level);
                    else if (EffectNameIs(effect, "ConsumeBody") && t.Kind == ZoneEntityKind.Npc)
                        DespawnNpc(t);
                }
            }
        }, allowDeadTarget: true);
        return null;
    }
    bool IsSelfCenteredHostile(SkillDefinition template) =>
        IsAuraTarget(template) || IsFrontAreaTarget(template);

    /// <summary>
    /// Shared target/refusal gates for debuffs and hostile utilities. AURA / FRONT_AREA centre on the caster
    /// and do not require an attackable primary (Mobius <c>AURA</c> / <c>FRONT_AREA</c>).
    /// </summary>
    bool TryResolveHostileSkillTarget(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        long now,
        out ZoneEntity? target,
        out string? error)
    {
        target = null;
        error = null;
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        bool selfCentered = IsSelfCenteredHostile(template);
        if (targetId <= 0)
        {
            if (selfCentered)
                return true;
            error = SkillProtocol.Target;
            return false;
        }
        if (!_byId.TryGetValue(targetId, out var picked) ||
            !IsAttackable(player, picked) ||
            !VisibleTo(player.ObjectId, targetId))
        {
            if (selfCentered)
                return true;
            error = SkillProtocol.Target;
            return false;
        }
        if (!request.CtrlPressed && NeedsForce(player, picked))
            return selfCentered; // aura proceeds without that primary; a single-target skill is silently refused
        if (HostilePlayableRefused(session, player, picked, badSkill: true) ||
            SiegeFriendRefused(session, player, picked) ||
            PeacePlayableRefused(session, player, picked))
            return selfCentered;
        if (!TryEnsureHostileSkillRange(session, player, picked, request, template, now, out var rangeError))
        {
            if (selfCentered)
                return true;
            error = rangeError;
            return false;
        }
        target = picked;
        return true;
    }

    /// <summary>MP, item consume, animation, reuse, and the hit-timer callback for a hostile skill.</summary>
    string? LaunchHostileSkill(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now,
        ZoneEntity? target)
    {
        if (ConsumeItemBlocked(session, player, levelData))
            return null;
        if (target != null)
            UpdatePvPStatus(player, target, now);
        ConsumeSkillMp(player, template, levelData.MpConsume);
        if (!ConsumeSkillItems(session, player, levelData))
            return null;
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;
        var anim = target ?? player;
        var cast = BuildCast(player, anim, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, anim, now);
        FanoutSkill(player, anim, cast, targetStatus: null, CasterMpStatus(player));
        EnterCombatStanceOnCast(player, template, level, now);
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        if (target != null)
            session.SelectedTargetId = target.ObjectId;
        ScheduleEffect(player, anim, template, level, now, hitMs, (_, at) =>
        {
            ApplyHostileAffect(player, target, template, levelData, level, at);
        });
        return null;
    }

    /// <summary>
    /// Mobius <c>doAttackHitByPole</c>: up to <c>AtkCountMax - 1</c> extra targets in melee reach,
    /// skipped entirely while <c>PolearmSingleTarget</c> (Focus Attack) is on the bar.
    /// </summary>
    void QueuePoleSplashHits(ZoneEntity player, ZoneEntity primary, PlayerCombatStats ps, bool soulshot, long due)
    {
        if (ps.Weapon != WeaponType.Pole || HasPolearmSingleTarget(player))
            return;
        int extra = Math.Max(0, ps.AtkCountMax - 1);
        if (extra <= 0)
            return;
        int queued = 0;
        foreach (var entity in _byId.Values)
        {
            if (queued >= extra)
                break;
            if (ReferenceEquals(entity, primary) || ReferenceEquals(entity, player))
                continue;
            if (entity.Dead || entity.CurHp <= 0)
                continue;
            if (!IsAttackable(player, entity))
                continue;
            if (entity.Kind == ZoneEntityKind.Player &&
                (!PlayerAutoAttackable(player, entity) || (player.Session != null &&
                    PeacePlayableRefused(player.Session, player, entity, notify: false))))
                continue;
            if (!ZoneCombat.Within(player.X, player.Y, entity.X, entity.Y, AttackReachAgainst(player, entity)))
                continue;
            if (!VisibleTo(player.ObjectId, entity.ObjectId))
                continue;
            var (damage, flags) = RollPlayerMelee(player, ps, entity, soulshot);
            QueueAttackerDamageLine(player, entity, damage, flags);
            _meleeHits.Add(new PendingMeleeHit(player.ObjectId, entity.ObjectId, damage, due, flags));
            queued++;
        }
    }


    /// <summary>
    /// Mobius Confuse (instant): chance-gated CONFUSED slot, then the victim attacks a random nearby creature.
    /// </summary>
    internal void ApplyConfuse(ZoneEntity caster, ZoneEntity target, SkillEffectCatalog.Effect effect, int skillId, long now)
    {
        if (target.Dead || target.CurHp <= 0)
            return;
        if (target.NpcType is "RaidBoss" or "GrandBoss" || target.RaidMinion)
            return;
        if (EffectChanceFails(effect))
            return;
        int timeSec = effect.ReadExtraInt("abnormalTime");
        if (timeSec <= 0)
            timeSec = effect.ReadExtraInt("time");
        if (timeSec <= 0)
            timeSec = 20;
        long until = now + timeSec * 1000L;
        target.Abnormals.RemoveAll(a => a.Slot.Equals("CONFUSED", StringComparison.OrdinalIgnoreCase));
        target.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = skillId,
            Value = 1,
            SkillLevel = 1,
            Slot = "CONFUSED",
            ExpiresAtUnixMs = until
        });
        _withAbnormals.Add(target.ObjectId);
        FanoutAbnormal(target);

        int range = 2000;
        var template = _game.Skills.Find(skillId);
        if (template != null && template.EffectRange > 0)
            range = template.EffectRange;
        var candidates = new List<ZoneEntity>();
        foreach (var entity in _byId.Values)
        {
            if (ReferenceEquals(entity, target) || entity.Dead || entity.CurHp <= 0)
                continue;
            if (entity.Kind == ZoneEntityKind.Npc && !entity.Attackable)
                continue;
            if (entity.Kind != ZoneEntityKind.Npc && entity.Kind != ZoneEntityKind.Player)
                continue;
            if (!ZoneCombat.InCastRange(target.X, target.Y, entity.X, entity.Y, range))
                continue;
            candidates.Add(entity);
        }
        if (candidates.Count == 0)
            return;
        var pick = candidates[_combatRandom.Next(candidates.Count)];
        if (target.Kind == ZoneEntityKind.Npc && target.Attackable)
        {
            AddHate(target, pick, hate: 10_000, allowNpcAttacker: true);
            if (!target.Running)
            {
                target.Running = true;
                FanoutMoveType(target);
            }
        }
        else if (target.Session is { } session)
        {
            session.AutoAttackTargetId = pick.ObjectId;
            session.SelectedTargetId = pick.ObjectId;
        }
    }

}
