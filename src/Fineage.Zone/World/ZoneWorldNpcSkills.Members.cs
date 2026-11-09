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
                ZoneLog.Info(
                    $"[npc-support] heal {skill.SkillId} npc={caster.NpcId} {before}->{target.CurHp} (+{skill.Amount})");
                break;
            case NpcSupportKind.Buff:
            case NpcSupportKind.Debuff:
            case NpcSupportKind.Immobilize:
            case NpcSupportKind.Silence:
                if (skill.AbnormalTimeSeconds <= 0)
                    return;
                int abnormal = skill.AbnormalLevel > 0 ? skill.AbnormalLevel : skill.Level;
                ZoneAbnormals.Apply(
                    target.Abnormals,
                    skill.SkillId,
                    abnormal,
                    skill.AbnormalTimeSeconds,
                    now,
                    skill.Level,
                    skill.Slot);
                if (kind is NpcSupportKind.Immobilize or NpcSupportKind.Silence)
                    StopTimedMove(target);
                FanoutAbnormal(target);
                ZoneLog.Info(
                    $"[npc-support] {kind} {skill.SkillId} lv{skill.Level} slot={skill.Slot} on={target.ObjectId}");
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
        int threshold = min >= max ? min : min + _combatRandom.Next(max - min + 1);
        if (_combatRandom.Next(100) >= threshold)
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

        var pick = candidates[_combatRandom.Next(candidates.Count)];
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
        ZoneLog.Info($"[npc-skill] cast {ai.SkillId} lv{level} npc={npc.NpcId} → player={player.ObjectId} hit={hitTime}ms");
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
                && !ZoneCombat.InCastRange(npc.X, npc.Y, target.X, target.Y, hit.EscapeRange + ZoneCombat.HitRangeTolerance))
                return;
            if (!ReferenceEquals(npc, target) && !CanSeeTarget(npc, target))
                return;
            apply(target, now);
            return;
        }

        if (hit.Combat == null || target.Kind != ZoneEntityKind.Player)
            return;

        int escape = Fineage.Game.Combat.CastRules.EscapeRange(hit.Combat.CastRange, hit.Combat.EffectRange);
        if (escape > 0 && !ZoneCombat.InCastRange(npc.X, npc.Y, target.X, target.Y, escape + ZoneCombat.HitRangeTolerance))
            return;
        if (!CanSeeTarget(npc, target))
            return;

        int damage = RollNpcSkillDamage(npc, target, hit.Combat, hit.Level);
        ZoneLog.Info($"[npc-skill] hit {hit.Combat.SkillId} lv{hit.Level} damage={damage}");
        if (damage <= 0)
            return;

        target.CurHp = Math.Max(0, target.CurHp - damage);
        FanoutVitals(target, PlayerVitalsStatus(target));
        target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        if (target.CurHp > 0)
            MaybeBreakCast(target, damage);
        if (target.CurHp <= 0)
        {
            npc.HateList.Remove(target.ObjectId);
            SetAggroTarget(npc, MostHated(npc));
            if (target.Session != null)
                target.Session.AutoAttackTargetId = 0;
            ApplyPlayerDeath(target, npc.ObjectId);
        }
    }

    /// <summary>Mobius NPC skill damage vs a player using template P.Atk/M.Atk and player defence.</summary>
    internal int RollNpcSkillDamage(ZoneEntity npc, ZoneEntity player, SkillCombatData combat, int level)
    {
        npc.CurMp = Math.Max(0, npc.CurMp - combat.MpAt(level));
        var ps = PlayerStats(player);
        double power = combat.PowerAt(level);
        if (!combat.IsPhysical)
        {
            bool spirit = TryNpcSpiritshot(npc);
            double mAtk = Math.Max(1d, npc.MAtk) * ZoneCombat.SpiritshotMAtkMultiplier(spirit ? 1 : 0);
            int magicLevel = combat.MagicLevelAt(level);
            if (magicLevel <= 0)
                magicLevel = npc.Level;
            // NPC mCritRate table not imported — use unarmed base 10‰ (Mobius default).
            var (damage, _) = ZoneCombat.RollMagicDamage(_combatRandom, mAtk, Math.Max(1d, ps.MDef), power,
                npc.Level, magicLevel, player.Level, mCritRate: 10);
            return damage;
        }

        bool ss = TryNpcSoulshot(npc);
        // Blow without blowEffect in catalog → physical skill formula (NPC Mortal Blow).
        return ZoneCombat.PhysicalSkillDamage(
            StatMods(npc).Apply(CombatStat.PAtk, npc.PAtk), Math.Max(1d, ps.PDef), power, ss, crit: false);
    }

    /// <summary>Mobius <c>Npc.rechargeShots(false, true)</c> for spiritshots.</summary>
    bool TryNpcSpiritshot(ZoneEntity npc)
    {
        if (npc.NpcSpiritShotLeft <= 0 || npc.NpcSpiritShotChance <= 0)
            return false;
        if (_combatRandom.Next(100) >= npc.NpcSpiritShotChance)
            return false;
        npc.NpcSpiritShotLeft--;
        return true;
    }

    void FanoutNpcMagicSkillUse(ZoneEntity npc, ZoneEntity target, int skillId, int level, int hitTime, int reuse)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            if (ReferenceEquals(watcher, target) ||
                watcher.Knownlist.Contains(npc.ObjectId) ||
                watcher.Knownlist.Contains(target.ObjectId))
            {
                netWatchers ??= [];
                netWatchers.Add(watcher.ObjectId);
            }
        }
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
        lock (_gate)
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
            return CastNpcSupportSkill(npc, target, skill, k, _clock().ToUnixTimeMilliseconds());
        }
    }

    /// <summary>Test hook: force Death Bomb (AoE + self-die) ignoring HP%/chance gates.</summary>
    internal bool CastNpcSuicideForTest(ZoneEntity npc, ZoneEntity hateTarget, NpcAiSupportSkill skill)
    {
        lock (_gate)
        {
            return CastNpcSuicideSkill(npc, hateTarget, skill, _clock().ToUnixTimeMilliseconds());
        }
    }
}
