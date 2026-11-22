using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void ApplyNpcSupportEffect(
        ZoneEntity caster,
        ZoneEntity target,
        NpcAiSupportSkill skill,
        NpcSupportKind kind,
        long now)
    {
        caster.CurMp = Math.Max(0, caster.CurMp - skill.Mp);
        switch (kind)
        {
            case NpcSupportKind.Heal:
                if (target.MaxHp <= 0 || skill.Amount <= 0)
                    return;
                int before = target.CurHp;
                target.CurHp = Math.Min(target.MaxHp, target.CurHp + skill.Amount);
                ZoneHotPathLogs.NpcSupportHeal(skill.SkillId, caster.NpcId, before, target.CurHp, skill.Amount);
                break;
            case NpcSupportKind.Buff:
            case NpcSupportKind.Debuff:
            case NpcSupportKind.Immobilize:
            case NpcSupportKind.Silence:
                if (skill.AbnormalTimeSeconds <= 0)
                    return;
                // Debuffs/control roll Mobius calcEffectSuccess so jewelry DefenceTrait resists apply.
                if (kind is not NpcSupportKind.Buff)
                {
                    var template = _game.Skills.Find(skill.SkillId);
                    if (template != null &&
                        template.TryLevelData(Math.Max(1, skill.Level), out var levelData) &&
                        !RollDebuffLand(caster, target, template, levelData, out _))
                        return;
                }
                int abnormal = skill.AbnormalLevel > 0 ? skill.AbnormalLevel : skill.Level;
                string slot = skill.Slot;
                if (slot.Length == 0)
                    slot = _game.Skills.Find(skill.SkillId)?.AbnormalType ?? "";
                ZoneAbnormals.Apply(
                    target.Abnormals,
                    skill.SkillId,
                    abnormal,
                    skill.AbnormalTimeSeconds,
                    now,
                    skill.Level,
                    slot);
                if (kind is NpcSupportKind.Immobilize or NpcSupportKind.Silence)
                    StopTimedMove(target);
                FanoutAbnormal(target);
                ZoneHotPathLogs.NpcSupportBuff(kind.ToString(), skill.SkillId, skill.Level, slot, target.ObjectId);
                break;
        }
    }

    /// <summary>
    /// Mobius <c>Npc.isSkillChance()</c>: <c>Rnd(100) &lt; Rnd(min, max)</c>. When true, try to cast
    /// instead of the melee swing.
    /// </summary>
    bool TryNpcSkillInsteadOfMelee(ZoneEntity npc, ZoneEntity player, long now)
    {
        if (npc.AiSkills is not { Count: > 0 } || npc.CastingUntilMs > now || npc.PendingHit != null)
            return false;
        int min = Math.Clamp(npc.MinSkillChance, 0, 100);
        int max = Math.Clamp(Math.Max(min, npc.MaxSkillChance), 0, 100);
        var rng = Rng(_combatRandom, npc);
        int threshold = min >= max ? min : min + rng.Next(max - min + 1);
        if (rng.Next(100) >= threshold)
            return false;

        var candidates = new List<(NpcAiSkill Skill, SkillCombatData Combat)>(npc.AiSkills.Count);
        foreach (var ai in npc.AiSkills)
        {
            if (!_skillCombat.TryGet(ai.SkillId, out var combat) || combat == null)
                continue;
            if (npc.SkillReuseUntilMs.TryGetValue(ai.SkillId, out long until) && until > now)
                continue;
            int level = Math.Clamp(ai.Level, 1, Math.Max(1, combat.Levels));
            int mp = combat.MpAt(level) + combat.MpInitialAt(level);
            if (npc.CurMp < mp)
                continue;
            candidates.Add((new NpcAiSkill(ai.SkillId, level), combat));
        }
        if (candidates.Count == 0)
            return false;

        var pick = candidates[rng.Next(candidates.Count)];
        return CastNpcAttackSkill(npc, player, pick.Skill, pick.Combat, now);
    }

    bool CastNpcAttackSkill(ZoneEntity npc, ZoneEntity player, NpcAiSkill ai, SkillCombatData combat, long now)
    {
        int level = ai.Level;
        int mpInitial = combat.MpInitialAt(level);
        if (npc.CurMp < mpInitial + combat.MpAt(level))
            return false;

        npc.CurMp = Math.Max(0, npc.CurMp - mpInitial);
        // NPC cast speed: physical skills scale by Pspd; magic keeps catalog hitTime until Cspd is imported.
        int hitTime = combat.Magic
            ? Math.Max(0, combat.HitTimeMs)
            : ZoneCombat.SkillTimeMs(combat.HitTimeMs, magic: false, mAtkSpd: 333, NpcPAtkSpd(npc));
        int reuse = combat.ReuseDelayMs;
        if (reuse > 0)
            npc.SkillReuseUntilMs[ai.SkillId] = now + reuse;

        npc.Heading = ZoneCombat.HeadingFrom(npc.X, npc.Y, player.X, player.Y);
        FanoutNpcMagicSkillUse(npc, player, ai.SkillId, level, hitTime, reuse);
        npc.CastingUntilMs = now + hitTime + Math.Max(0, combat.CoolTimeMs);
        SetPendingHit(npc, new PendingSkillHit(player.ObjectId, 0, now + hitTime, combat, level));
        npc.NextMobAttackMs = npc.CastingUntilMs;
        ZoneHotPathLogs.NpcSkillCast(ai.SkillId, level, npc.NpcId, player.ObjectId, hitTime);
        if (hitTime == 0)
            ResolveNpcPendingHit(npc, now);
        return true;
    }

    void ResolveNpcPendingHit(ZoneEntity npc, long now)
    {
        var hit = npc.PendingHit;
        SetPendingHit(npc, null);
        if (hit == null || npc.Dead)
            return;
        if (!_byId.TryGetValue(hit.TargetId, out var target) || target.Dead)
            return;

        // Support skills (heal/buff/debuff) carry Apply; attack skills carry Combat.
        if (hit.Apply is Action<ZoneEntity, long> apply)
        {
            if (hit.EscapeRange > 0
                && !ZoneCombat.Within(npc.X, npc.Y, target.X, target.Y, SkillCastReach(hit.EscapeRange, npc, target)))
                return;
            if (!ReferenceEquals(npc, target) && !CanSeeTarget(npc, target))
                return;
            FanoutSkillLaunched(npc, hit.SkillId, hit.Level, target);
            apply(target, now);
            return;
        }

        if (hit.Combat == null)
            return;
        bool summonCaster = npc.Summon != null;
        if (target.Kind != ZoneEntityKind.Player && !(summonCaster && target.Kind == ZoneEntityKind.Npc))
            return;

        int escape = Fineage.Game.Combat.CastRules.EscapeRange(hit.Combat.CastRange, hit.Combat.EffectRange);
        if (escape > 0 && !ZoneCombat.Within(npc.X, npc.Y, target.X, target.Y, SkillCastReach(escape, npc, target)))
            return;
        if (!CanSeeTarget(npc, target))
            return;

        FanoutSkillLaunched(npc, hit.Combat.SkillId, hit.Level, target);
        int damage = RollNpcSkillDamage(npc, target, hit.Combat, hit.Level);
        ZoneHotPathLogs.NpcSkillHit(hit.Combat.SkillId, hit.Level, damage);
        // Mobius onCreatureDamageReceived → TriggerSkillByDamage runs before the summon branch splits the victim kinds.
        if (damage > 0 && target.Kind == ZoneEntityKind.Player)
            RunDamageTriggers(target, npc, damage);
        if (summonCaster)
        {
            var targetPc = CreditOf(target);
            if (targetPc.Kind == ZoneEntityKind.Player && OwnerOf(npc) is { } caster && !ReferenceEquals(caster, targetPc))
                UpdatePvPStatus(caster, targetPc, now);
            if (target.Summon != null)
            {
                DamageSummon(npc, target, damage, now);
                return;
            }
            if (target.Kind == ZoneEntityKind.Npc)
            {
                if (damage > 0)
                    DamageNpcBy(npc, target, damage, now);
                return;
            }
        }
        if (damage <= 0 || DuelBlocksHit(npc, target))
            return;

        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, PlayerVitalsStatus(target));
        SendVictimHitLines(npc, target, damage, 0);
        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        // Mobius onCreatureDamageReceived → TriggerSkillByDamage (Mirage 445).
        RunDamageTriggers(target, npc, damage);
        if (target.CurHp > 0)
            MaybeBreakCast(target, damage);
        if (target.CurHp <= 0)
        {
            if (summonCaster)
                npc.Summon!.TargetId = 0;
            else
            {
                npc.HateList.Remove(target.ObjectId);
                SetAggroTarget(npc, MostHated(npc));
            }
            if (target.Session != null)
                target.Intent.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, npc.ObjectId);
        }
    }

    /// <summary>Mobius NPC skill damage vs a player using template P.Atk/M.Atk and player defence.</summary>
    internal int RollNpcSkillDamage(ZoneEntity npc, ZoneEntity player, SkillCombatData combat, int level)
    {
        npc.CurMp = Math.Max(0, npc.CurMp - combat.MpAt(level));
        bool npcTarget = player.Kind == ZoneEntityKind.Npc;
        double targetMDef, targetPDef;
        if (npcTarget)
        {
            targetMDef = Math.Max(1d, player.MDef);
            targetPDef = NpcPDef(player);
        }
        else
        {
            var ps = PlayerStats(player);
            targetMDef = Math.Max(1d, ps.MDef);
            targetPDef = Math.Max(1d, ps.PDef);
        }
        double power = combat.PowerAt(level);
        if (!combat.IsPhysical)
        {
            bool spirit = TryNpcSpiritshot(npc);
            double mAtk = Math.Max(1d, npc.MAtk) * ZoneCombat.SpiritshotMAtkMultiplier(spirit ? 1 : 0);
            int magicLevel = combat.MagicLevelAt(level);
            if (magicLevel <= 0)
                magicLevel = npc.Level;
            // NPC mCritRate table not imported — use unarmed base 10‰ (Mobius default).
            var (damage, _) = ZoneCombat.RollMagicDamage(Rng(_combatRandom, npc), mAtk, targetMDef, power,
                npc.Level, magicLevel, player.Level, mCritRate: 10);
            return damage;
        }

        bool ss = TryNpcSoulshot(npc);
        // Blow without blowEffect in catalog → physical skill formula (NPC Mortal Blow).
        return ZoneCombat.PhysicalSkillDamage(
            StatMods(npc).Apply(CombatStat.PAtk, npc.PAtk), targetPDef, power, ss, crit: false);
    }

    /// <summary>Mobius <c>Npc.rechargeShots(false, true)</c> for spiritshots.</summary>
    bool TryNpcSpiritshot(ZoneEntity npc)
    {
        if (npc.NpcSpiritShotLeft <= 0 || npc.NpcSpiritShotChance <= 0)
            return false;
        if (Rng(_combatRandom, npc).Next(100) >= npc.NpcSpiritShotChance)
            return false;
        npc.NpcSpiritShotLeft--;
        FanoutNpcShotGlow(npc, soul: false);
        return true;
    }

    void FanoutNpcMagicSkillUse(ZoneEntity npc, ZoneEntity target, int skillId, int level, int hitTime, int reuse)
    {
        var netWatchers = ActorAudience(npc, target);
        if (netWatchers is { Count: > 0 })
        {
            NetBridge?.FanoutMagicSkillUse(netWatchers, new MagicSkillUseMsg
            {
                CasterObjectId = npc.ObjectId,
                TargetObjectId = target.ObjectId,
                SkillId = skillId,
                SkillLevel = level,
                HitTimeMs = hitTime,
                ReuseDelayMs = reuse
            });
        }
    }

    /// <summary>Test hook: force a support cast (heal/buff/debuff) ignoring AI chance gates.</summary>
    internal bool CastNpcSupportForTest(ZoneEntity npc, ZoneEntity target, NpcAiSupportSkill skill, string kind)
    {
        using (_affinity.Step())
        {
            var k = kind.ToLowerInvariant() switch
            {
                "heal" => NpcSupportKind.Heal,
                "buff" => NpcSupportKind.Buff,
                "debuff" => NpcSupportKind.Debuff,
                "immobilize" => NpcSupportKind.Immobilize,
                "silence" => NpcSupportKind.Silence,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return CastNpcSupportSkill(npc, target, skill, k, NowUnixMs());
        }
    }

    /// <summary>Test hook: force Death Bomb (AoE + self-die) ignoring HP%/chance gates.</summary>
    internal bool CastNpcSuicideForTest(ZoneEntity npc, ZoneEntity hateTarget, NpcAiSupportSkill skill)
    {
        using (_affinity.Step())
        {
            return CastNpcSuicideSkill(npc, hateTarget, skill, NowUnixMs());
        }
    }
}
