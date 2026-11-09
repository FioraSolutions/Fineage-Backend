using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius AttackableAI skill cast: HEAL / BUFF / DEBUFF scopes plus ATTACK chance vs min/maxSkillChance
/// through skill-combat formulas against the hate target.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius AISkillScope.SUICIDE: when HP &lt; 30%, skill-chance cast Death Bomb (aura MagicalAttack)
    /// then <c>doDie(self)</c>. Hate target must be inside <c>affectRange</c>.
    /// </summary>
    bool TryNpcSuicideSkill(ZoneEntity npc, ZoneEntity player, long now)
    {
        if (npc.AiSuicideSkills is not { Count: > 0 } || npc.CastingUntilMs > now || npc.PendingHit != null)
            return false;
        if (npc.MaxHp <= 0 || (int)(npc.CurHp * 100d / npc.MaxHp) >= 30)
            return false;

        int min = Math.Clamp(npc.MinSkillChance, 0, 100);
        int max = Math.Clamp(Math.Max(min, npc.MaxSkillChance), 0, 100);
        int threshold = min >= max ? min : min + _combatRandom.Next(max - min + 1);
        if (_combatRandom.Next(100) >= threshold)
            return false;

        var ready = ReadySupportSkills(npc, npc.AiSuicideSkills, now);
        if (ready.Count == 0)
            return false;
        var pick = ready[_combatRandom.Next(ready.Count)];
        int range = pick.AffectRange > 0 ? pick.AffectRange : 150;
        if (!ZoneCombat.Within(npc.X, npc.Y, player.X, player.Y, range))
            return false;

        return CastNpcSuicideSkill(npc, player, pick, now);
    }

    bool CastNpcSuicideSkill(ZoneEntity npc, ZoneEntity hateTarget, NpcAiSupportSkill skill, long now)
    {
        npc.CurMp = Math.Max(0, npc.CurMp - skill.MpInitial);
        int hitTime = Math.Max(0, skill.HitTimeMs);
        if (skill.ReuseDelayMs > 0)
            npc.SkillReuseUntilMs[skill.SkillId] = now + skill.ReuseDelayMs;

        npc.Heading = ZoneCombat.HeadingFrom(npc.X, npc.Y, hateTarget.X, hateTarget.Y);
        FanoutNpcMagicSkillUse(npc, hateTarget, skill.SkillId, skill.Level, hitTime, skill.ReuseDelayMs);
        npc.CastingUntilMs = now + hitTime;
        npc.NextMobAttackMs = npc.CastingUntilMs;
        int range = skill.AffectRange > 0 ? skill.AffectRange : 150;
        int creditId = hateTarget.ObjectId;
        ZoneLog.Info(
            $"[npc-suicide] cast {skill.SkillId} lv{skill.Level} npc={npc.NpcId} power={skill.Power} range={range} hit={hitTime}ms");

        // Pending on self so mid-cast hate-target death still detonates (Mobius activateSkill → doDie).
        SetPendingHit(npc, new PendingSkillHit(npc.ObjectId, 0, now + hitTime, null, skill.Level)
        {
            SkillId = skill.SkillId,
            EscapeRange = 0,
            TargetIsNpc = true,
            Apply = (Action<ZoneEntity, long>)((_, at) =>
            {
                npc.CurMp = Math.Max(0, npc.CurMp - skill.Mp);
                ApplyNpcSuicideBlast(npc, skill, range, at);
                ZoneEntity? credit = _byId.TryGetValue(creditId, out var c) && c.Kind == ZoneEntityKind.Player ? c : null;
                npc.CurHp = 0;
                RemoveDeadNpc(npc, killer: credit);
                if (credit != null)
                    GrantKillReward(credit, npc);
            })
        });
        if (hitTime == 0)
            ResolveNpcPendingHit(npc, now);
        return true;
    }

    void ApplyNpcSuicideBlast(ZoneEntity npc, NpcAiSupportSkill skill, int range, long now)
    {
        if (skill.Power <= 0)
            return;
        foreach (var target in Players().ToList())
        {
            if (target.Dead || target.CurHp <= 0
                || !SameInstance(npc.ObjectId, target.ObjectId)
                || !ZoneCombat.Within(npc.X, npc.Y, target.X, target.Y, range))
                continue;

            var ps = PlayerStats(target);
            bool spirit = TryNpcSpiritshot(npc);
            double mAtk = Math.Max(1d, npc.MAtk) * ZoneCombat.SpiritshotMAtkMultiplier(spirit ? 1 : 0);
            var (damage, _) = ZoneCombat.RollMagicDamage(
                _combatRandom, mAtk, Math.Max(1d, ps.MDef), skill.Power,
                npc.Level, magicLevel: npc.Level, target.Level, mCritRate: 10);
            if (damage <= 0)
                continue;

            target.CurHp = Math.Max(0, target.CurHp - damage);
            FanoutVitals(target, PlayerVitalsStatus(target));
            target.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
            if (target.CurHp > 0)
                MaybeBreakCast(target, damage);
            if (target.CurHp <= 0)
            {
                if (target.Session != null)
                    target.Session.AutoAttackTargetId = 0;
                ApplyPlayerDeath(target, npc.ObjectId);
            }
        }
    }

    /// <summary>
    /// Mobius HEAL scope: self-heal when wounded, else heal a wounded same-clan NPC in cast range.
    /// </summary>
    bool TryNpcHealSkill(ZoneEntity npc, long now)
    {
        if (npc.AiHealSkills is not { Count: > 0 } || npc.CastingUntilMs > now || npc.PendingHit != null)
            return false;

        var ready = ReadySupportSkills(npc, npc.AiHealSkills, now);
        if (ready.Count == 0)
            return false;
        var pick = ready[_combatRandom.Next(ready.Count)];

        // Self heal: Rnd(100) < (100 - hp%) / 3
        if (npc.MaxHp > 0 && npc.CurHp < npc.MaxHp)
        {
            double pct = npc.CurHp * 100d / npc.MaxHp;
            if (_combatRandom.Next(100) < (int)((100d - pct) / 3d))
                return CastNpcSupportSkill(npc, npc, pick, NpcSupportKind.Heal, now);
        }

        // Clan heal: living wounded clan mate, Rnd(100) < (100 - hp%) / 10
        if (npc.Clans is not { Count: > 0 })
            return false;
        const int healRange = 600;
        foreach (var other in _byId.Values)
        {
            if (other.Kind != ZoneEntityKind.Npc || ReferenceEquals(other, npc) || other.CurHp <= 0 || other.Dead
                || other.MaxHp <= 0 || other.CurHp >= other.MaxHp
                || other.Clans == null || !other.Clans.Any(c => npc.Clans.Contains(c))
                || !SameInstance(npc.ObjectId, other.ObjectId)
                || !ZoneCombat.Within(npc.X, npc.Y, other.X, other.Y, healRange))
                continue;
            double matePct = other.CurHp * 100d / other.MaxHp;
            if (_combatRandom.Next(100) >= (int)((100d - matePct) / 10d))
                continue;
            if (!CanSeeTarget(npc, other))
                continue;
            return CastNpcSupportSkill(npc, other, pick, NpcSupportKind.Heal, now);
        }
        return false;
    }

    /// <summary>Mobius BUFF scope (self): cast when the NPC is not already affected by that skill id.</summary>
    bool TryNpcSelfBuffSkill(ZoneEntity npc, long now)
    {
        if (npc.AiBuffSkills is not { Count: > 0 } || npc.CastingUntilMs > now || npc.PendingHit != null)
            return false;

        var ready = new List<NpcAiSupportSkill>();
        foreach (var skill in ReadySupportSkills(npc, npc.AiBuffSkills, now))
        {
            if (npc.Abnormals.Any(a => a.Id == skill.SkillId))
                continue;
            ready.Add(skill);
        }
        if (ready.Count == 0)
            return false;
        var pick = ready[_combatRandom.Next(ready.Count)];
        return CastNpcSupportSkill(npc, npc, pick, NpcSupportKind.Buff, now);
    }

    /// <summary>Mobius IMMOBILIZE scope: ~15% chance (sleep/root/stun/paralyze).</summary>
    bool TryNpcImmobilizeSkill(ZoneEntity npc, ZoneEntity player, long now)
    {
        if (IsImmobilized(player))
            return false;
        return TryNpcControlSkill(npc, player, npc.AiImmobilizeSkills, chancePercent: 15, NpcSupportKind.Immobilize, now);
    }

    /// <summary>Mobius COT silence scope: ~20% chance.</summary>
    bool TryNpcSilenceSkill(ZoneEntity npc, ZoneEntity player, long now)
    {
        if (IsSilenced(player))
            return false;
        return TryNpcControlSkill(npc, player, npc.AiSilenceSkills, chancePercent: 20, NpcSupportKind.Silence, now);
    }

    /// <summary>Mobius DEBUFF scope: ~30% chance to land one debuff on the hate target.</summary>
    bool TryNpcDebuffSkill(ZoneEntity npc, ZoneEntity player, long now)
    {
        return TryNpcControlSkill(npc, player, npc.AiDebuffSkills, chancePercent: 30, NpcSupportKind.Debuff, now);
    }

    bool TryNpcControlSkill(
        ZoneEntity npc,
        ZoneEntity player,
        IReadOnlyList<NpcAiSupportSkill>? skills,
        int chancePercent,
        NpcSupportKind kind,
        long now)
    {
        if (skills is not { Count: > 0 } || npc.CastingUntilMs > now || npc.PendingHit != null)
            return false;
        if (_combatRandom.Next(100) >= chancePercent)
            return false;

        var ready = new List<NpcAiSupportSkill>();
        foreach (var skill in ReadySupportSkills(npc, skills, now))
        {
            if (player.Abnormals.Any(a => a.Id == skill.SkillId))
                continue;
            ready.Add(skill);
        }
        if (ready.Count == 0)
            return false;
        var pick = ready[_combatRandom.Next(ready.Count)];
        return CastNpcSupportSkill(npc, player, pick, kind, now);
    }

    static List<NpcAiSupportSkill> ReadySupportSkills(
        ZoneEntity npc,
        IReadOnlyList<NpcAiSupportSkill> skills,
        long now)
    {
        var ready = new List<NpcAiSupportSkill>(skills.Count);
        foreach (var skill in skills)
        {
            if (npc.SkillReuseUntilMs.TryGetValue(skill.SkillId, out long until) && until > now)
                continue;
            if (npc.CurMp < skill.MpInitial + skill.Mp)
                continue;
            ready.Add(skill);
        }
        return ready;
    }

    enum NpcSupportKind { Heal, Buff, Debuff, Immobilize, Silence }

    internal static readonly HashSet<string> ImmobilizeSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "SLEEP", "ROOT_MAGICALLY", "ROOT_PHYSICALLY", "STUN", "PARALYZE"
    };

    /// <summary>Slots that also block skill use (root still allows casting).</summary>
    internal static readonly HashSet<string> ActionBlockSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "SLEEP", "STUN", "PARALYZE"
    };

    internal static readonly HashSet<string> SilenceSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "SILENCE", "SILENCE_PHYSICAL", "SILENCE_ALL"
    };

    internal static bool IsImmobilized(ZoneEntity entity) =>
        entity.Abnormals.Any(a => a.Slot.Length > 0 && ImmobilizeSlots.Contains(a.Slot));

    internal static bool IsActionBlocked(ZoneEntity entity) =>
        entity.Abnormals.Any(a => a.Slot.Length > 0 && ActionBlockSlots.Contains(a.Slot));

    internal static bool IsSilenced(ZoneEntity entity) =>
        entity.Abnormals.Any(a => a.Slot.Length > 0 && SilenceSlots.Contains(a.Slot));

    bool CastNpcSupportSkill(
        ZoneEntity npc,
        ZoneEntity target,
        NpcAiSupportSkill skill,
        NpcSupportKind kind,
        long now)
    {
        npc.CurMp = Math.Max(0, npc.CurMp - skill.MpInitial);
        int hitTime = Math.Max(0, skill.HitTimeMs);
        if (skill.ReuseDelayMs > 0)
            npc.SkillReuseUntilMs[skill.SkillId] = now + skill.ReuseDelayMs;

        npc.Heading = ZoneCombat.HeadingFrom(npc.X, npc.Y, target.X, target.Y);
        FanoutNpcMagicSkillUse(npc, target, skill.SkillId, skill.Level, hitTime, skill.ReuseDelayMs);
        npc.CastingUntilMs = now + hitTime;
        npc.NextMobAttackMs = npc.CastingUntilMs;
        SetPendingHit(npc, new PendingSkillHit(target.ObjectId, 0, now + hitTime, null, skill.Level)
        {
            SkillId = skill.SkillId,
            EscapeRange = ReferenceEquals(npc, target) ? 0 : 600,
            TargetIsNpc = target.Kind == ZoneEntityKind.Npc,
            Apply = (Action<ZoneEntity, long>)((t, at) => ApplyNpcSupportEffect(npc, t, skill, kind, at))
        });
        ZoneLog.Info(
            $"[npc-support] cast {kind} {skill.SkillId} lv{skill.Level} npc={npc.NpcId} → {target.ObjectId} hit={hitTime}ms");
        if (hitTime == 0)
            ResolveNpcPendingHit(npc, now);
        return true;
    }

}
