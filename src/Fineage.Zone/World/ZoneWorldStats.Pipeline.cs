using Fineage.Game;
using Fineage.Game.Skills;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius reuse: base · (dReuse / mReuse / pReuse) · 333 / atkSpd, then Skill Mastery may snap it to 100 ms.
    /// </summary>
    internal int SkillReuseMsFor(ZoneEntity player, SkillDefinition template, int baseMs) =>
        SkillReuseMsFor(player, template.MagicType == 1, baseMs, SkillPersistRules.IsDance(template.SkillId));

    internal int SkillReuseMsFor(ZoneEntity player, bool magic, int baseMs, bool dance = false)
    {
        var ps = PlayerStats(player);
        double mul = dance ? ps.DReuse : magic ? ps.MReuse : ps.PReuse;
        int reuse = ZoneCombat.SkillReuseMs(baseMs, magic, ps.MAtkSpd, ps.PAtkSpd, mul);
        if (reuse <= 10)
            return reuse;
        var attrs = EffectiveAttributes(player);
        return ZoneCombat.RollSkillMastery(
            Rng(_combatRandom, player), ps.SkillCritical, ps.SkillCriticalProbability, attrs.StrBonus, attrs.IntBonus)
            ? 100
            : reuse;
    }

    /// <summary>
    /// Mobius <c>CreatureStat.getMpConsume</c>: <c>calcStat(*MpConsumeRate, rawMp)</c> so mul/add land on the skill cost.
    /// </summary>
    internal int SkillMpCost(ZoneEntity player, SkillDefinition? template, int rawMp)
    {
        if (rawMp <= 0)
            return 0;
        if (template == null)
            return rawMp;
        var mods = PlayerStatMods(player);
        var stat = SkillPersistRules.IsDance(template.SkillId)
            ? CombatStat.DanceMpConsumeRate
            : template.MagicType == 1
                ? CombatStat.MagicalMpConsumeRate
                : CombatStat.PhysicalMpConsumeRate;
        return Math.Max(0, (int)mods.Apply(stat, rawMp));
    }

    internal void ConsumeSkillMp(ZoneEntity player, SkillDefinition template, int rawMp) =>
        player.CurMp = Math.Max(0, player.CurMp - SkillMpCost(player, template, rawMp));

    /// <summary>Zero the shield rate when the attacker is outside the Mobius shield cone.</summary>
    internal double ShieldRateInArc(ZoneEntity defender, ZoneEntity attacker, double rate, int shieldDefAngleBonus)
    {
        if (rate <= 1e-6)
            return 0;
        return ZoneCombat.ShieldArcCovers(
            defender.X, defender.Y, defender.Heading, attacker.X, attacker.Y, shieldDefAngleBonus)
            ? rate
            : 0;
    }

    /// <summary>
    /// Mobius reflectDam / absorbDam after a hit lands. Reflect is not itself reflected. Bows skip both.
    /// </summary>
    internal void ApplyReflectAndAbsorb(ZoneEntity attacker, ZoneEntity target, int damage)
    {
        if (damage <= 0 || attacker.ObjectId == target.ObjectId)
            return;
        bool bow = attacker.Kind == ZoneEntityKind.Player &&
                   PlayerStats(attacker).Weapon == WeaponType.Bow;

        double reflectPct = target.Kind == ZoneEntityKind.Player
            ? PlayerStats(target).ReflectDam
            : StatMods(target).Apply(CombatStat.ReflectDam, 0d);
        int reflected = ZoneCombat.ReflectedDamage(damage, reflectPct, target.MaxHp, bow);
        if (reflected > 0 && attacker.CurHp > 0)
        {
            attacker.CurHp = Math.Max(0, attacker.CurHp - reflected);
            if (attacker.Kind == ZoneEntityKind.Player)
                FanoutVitals(attacker, PlayerVitalsStatus(attacker));
            else
                FanoutVitals(attacker, StatusFor(attacker));
            if (attacker.CurHp <= 0)
                KillFromReflect(attacker, target);
        }

        if (bow || attacker.Kind != ZoneEntityKind.Player || attacker.CurHp <= 0)
            return;
        var aps = PlayerStats(attacker);
        int absorb = ZoneCombat.AbsorbedDamage(damage, aps.AbsorbDam, attacker.MaxHp - attacker.CurHp, bow);
        if (absorb <= 0)
            return;
        attacker.CurHp = Math.Min(attacker.MaxHp, attacker.CurHp + absorb);
        FanoutVitals(attacker, PlayerVitalsStatus(attacker));
    }

    void KillFromReflect(ZoneEntity attacker, ZoneEntity target)
    {
        if (attacker.Kind == ZoneEntityKind.Player)
        {
            if (attacker.Session != null)
                attacker.Session.AutoAttackTargetId = 0;
            ApplyPlayerDeath(attacker, target.ObjectId);
        }
        else if (attacker.Kind == ZoneEntityKind.Npc && attacker.Summon == null)
        {
            RemoveDeadNpc(attacker, killer: target.Kind == ZoneEntityKind.Player ? target : CreditOf(target));
        }
    }

    internal int HealedAmount(ZoneEntity target, int rawHeal)
    {
        if (rawHeal <= 0)
            return 0;
        if (target.Kind != ZoneEntityKind.Player)
            return rawHeal;
        return Math.Max(0, (int)PlayerStatMods(target).Apply(CombatStat.HealEffect, rawHeal));
    }
}
