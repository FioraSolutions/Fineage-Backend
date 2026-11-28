using Fineage.Game;

namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
    /// <summary>Mobius <c>PlayerConfig.MAX_MCRIT_RATE</c> (default 200 ‰), applied by <c>CreatureStat.getMCriticalHit</c>.</summary>
    public const int MaxMCritRate = 200;

    /// <summary>Mobius <c>calcMagicDam</c> core: <c>(91 · √mAtk) / mDef · power</c> (mAtk already ×2 / ×4 for shots).</summary>
    public static double MagicBaseDamage(double mAtk, double mDef, double power) =>
        91d * Math.Sqrt(Math.Max(0, mAtk)) / Math.Max(1d, mDef) * Math.Max(0, power);

    /// <summary>
    /// Mobius <c>calcMagicDam</c> after the base: failure branch (player casters only), crit ×3 (×2.5 player→player)
    /// then <c>MAGIC_CRIT_DMG</c>, then the weapon random multiplier, truncated to int (0 = no hit). A failed magic-success roll never also crits.
    /// </summary>
    public static int MagicDamageFor(double baseDamage, MagicOutcome outcome, bool playerVsPlayer,
        double magicCritDmg = 1d, double randomMultiplier = 1d)
    {
        double damage = baseDamage;
        switch (outcome)
        {
            case MagicOutcome.HalfFail:
                damage /= 2;
                break;
            case MagicOutcome.Resisted:
                damage = 1;
                break;
            case MagicOutcome.Critical:
                damage *= playerVsPlayer ? 2.5 : 3;
                damage *= Math.Max(0, magicCritDmg);
                break;
        }

        damage *= randomMultiplier;
        // Mobius MagicalDamage effect: (int) damage, and nothing happens when it truncates to 0 (a resisted 1.0 × 0.95 deals no damage).
        return Math.Max(0, (int)damage);
    }

    /// <summary>
    /// Mobius <c>Creature.getRandomDamageMultiplier</c> percent: the weapon <c>RANDOM_DAMAGE</c> stat, or
    /// <c>5 + (int)√level</c> with no weapon.
    /// </summary>
    public static int MagicRandomPercent(bool armed, int weaponRandomDamage, int casterLevel) =>
        armed ? weaponRandomDamage : 5 + (int)Math.Sqrt(casterLevel);

    public static (int Damage, MagicOutcome Outcome) RollMagicDamage(Random rnd, double mAtk, double mDef, double power,
        int casterLevel, int magicLevel, int targetLevel, int mCritRate, bool targetIsPlayer = false,
        double magicCritDmg = 1d, bool casterIsPlayer = true, int randomPercent = 0)
    {
        bool mcrit = RollMCrit(rnd, Math.Min(mCritRate, MaxMCritRate));
        double baseDamage = MagicBaseDamage(mAtk, mDef, power);
        var outcome = MagicOutcome.Normal;
        if (!RollMagicSuccess(rnd, targetLevel, magicLevel))
        {
            // Mobius only changes damage on a failed roll for a player caster; an NPC caster keeps full damage and no crit.
            if (casterIsPlayer)
            {
                outcome = RollMagicSuccess(rnd, targetLevel, magicLevel) && targetLevel - casterLevel <= 9
                    ? MagicOutcome.HalfFail
                    : MagicOutcome.Resisted;
            }
        }
        else if (mcrit)
        {
            outcome = MagicOutcome.Critical;
        }

        double random = randomPercent > 0 ? 1 + (rnd.Next(2 * randomPercent + 1) - randomPercent) / 100d : 1d;
        return (MagicDamageFor(baseDamage, outcome, casterIsPlayer && targetIsPlayer, magicCritDmg, random), outcome);
    }

    public enum BlowOutcome { Failed, Hit, Critical }
    public enum LethalOutcome { None, Full, Half }

    /// <summary>Mobius blow position factor: front 1, side 1.5, behind 2 (calcBlowSuccess sideMod).</summary>
    public static double BlowSideMod(bool front, bool behind) => front ? 1 : behind ? 2 : 1.5;

    /// <summary>Mobius calcBlowSuccess: Rnd(100) &lt; calcStat(BLOW_RATE, blowChance · DEX · sideMod).</summary>
    public static bool RollBlowSuccess(Random rnd, int blowChance, double dexBonus, bool front, bool behind,
        double blowRateMod = 1d) =>
        rnd.Next(100) < blowChance * dexBonus * BlowSideMod(front, behind) * Math.Max(0, blowRateMod);

    /// <summary>
    /// Mobius calcBlowDamage (FatalBlow): 77·(power + pAtk·ss1.458)/pDef · proximity (behind 1.2, side 1.1, front 1).
    /// Backstab (calcBackstabDamage): 77·(power + pAtk)/pDef · ss1.458 · proximity. Shield block adds sDef, perfect → 1.
    /// Then weapon random spread and skill crit ×2 (rate = baseCritRate·10·STR bonus ‰), plus the flat
    /// <c>critDmgAdd</c> term Mobius adds unconditionally in <c>calcBlowDamage</c>: <c>critDmgAdd · 6.1 · 77 / pDef</c>.
    /// </summary>
    public static (int Damage, BlowOutcome Outcome) RollBlowDamage(Random rnd, BlowEffect effect, double pAtk, double pDef,
        double power, bool soulshot, bool front, bool behind, int randomPercent, int baseCritRate, double strBonus,
        double shieldRate = 0, int shieldDef = 0, double critDamageMod = 1d, double weaponTraitBonus = 1d,
        double critDamageAdd = 0)
    {
        var shield = RollShield(rnd, shieldRate, false);
        if (shield == ShieldResult.Perfect)
            return (1, BlowOutcome.Hit);
        double def = Math.Max(1d, pDef + (shield == ShieldResult.Block ? shieldDef : 0));
        double proximity = behind ? 1.2 : front ? 1 : 1.1;
        double ss = soulshot ? 1.458 : 1;
        double damage = effect == BlowEffect.Backstab
            ? 77d * (power + pAtk) / def * ss * proximity
            : 77d * (power + pAtk * ss) / def * proximity;
        if (randomPercent > 0)
            damage *= 1 + rnd.Next(-randomPercent, randomPercent + 1) / 100d;
        bool crit = RollSkillCrit(rnd, baseCritRate, strBonus);
        if (crit)
        {
            damage *= 2 * Math.Max(0, critDamageMod);
            // Mobius: criticalAddMod is summed after the multipliers, so Vicious Stance / Critical Power /
            // crit-damage SA add a flat term instead of scaling the blow.
            if (critDamageAdd > 0)
                damage += critDamageAdd * 6.1 * 77 / def;
        }
        damage *= Math.Max(0, weaponTraitBonus);
        if (damage <= 0)
            return (0, crit ? BlowOutcome.Critical : BlowOutcome.Hit);
        return (Math.Max(1, (int)damage), crit ? BlowOutcome.Critical : BlowOutcome.Hit);
    }

    /// <summary>
    /// Mobius Lethal effect vs a monster: skipped when skill magicLvl &lt; target level − 6; roll = Rnd(1e6)/1e4;
    /// &lt; full → HP 1, &lt; full+half → HP ×0.5. Returns the extra HP to remove after <paramref name="hpAfterDamage"/>.
    /// </summary>
    public static (int ExtraDamage, LethalOutcome Outcome) RollLethal(Random rnd, int fullLethal, int halfLethal,
        int magicLevel, int targetLevel, int hpAfterDamage, bool lethalable = true)
    {
        if ((fullLethal <= 0 && halfLethal <= 0) || !lethalable || hpAfterDamage <= 1 || magicLevel < targetLevel - 6)
            return (0, LethalOutcome.None);
        double full = Math.Clamp(fullLethal, 0, 100), half = Math.Clamp(full + halfLethal, 0, 100);
        double roll = rnd.Next(1000000) / 10000.0;
        if (roll < full)
            return (hpAfterDamage - 1, LethalOutcome.Full);
        if (roll < half)
            return (hpAfterDamage - (int)(hpAfterDamage * 0.5), LethalOutcome.Half);
        return (0, LethalOutcome.None);
    }

    /// <summary>Mobius calcMagicDam M.Atk multiplier: blessed spiritshot ×4, spiritshot ×2.</summary>
    public static double SpiritshotMAtkMultiplier(int charge) => charge >= 2 ? 4d : charge == 1 ? 2d : 1d;

    /// <summary>Interlude <c>calcMagicDam</c> core: 91·√mAtk / mDef · power (no fail / crit).</summary>
    public static int MagicDamage(double mAtk, double mDef, double power) =>
        Math.Max(1, (int)MagicBaseDamage(mAtk, mDef, power));

    /// <summary>
    /// Mobius CT0 <c>Formulas.calcPhysDam</c> core (no crit/shots/random/elements):
    /// <c>76 * pAtk * proximity / pDef</c>, min 1. Proximity is 1 front, 1.1 side, 1.2 behind.
    /// </summary>
    public const double PhysDamCoefficient = 76d;

    public static double ProximityBonus(SkillPosition position) => position switch
    {
        SkillPosition.Behind => 1.2,
        SkillPosition.Side => 1.1,
        _ => 1d,
    };

    public static int PhysicalDamage(double pAtk, double pDef, double proximity = 1d) =>
        Math.Max(1, (int)(PhysDamCoefficient * Math.Max(0d, pAtk) * Math.Max(0d, proximity) / Math.Max(1d, pDef)));
}
