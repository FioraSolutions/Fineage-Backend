using Fineage.Game;

namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
    public static (int Damage, MagicOutcome Outcome) RollMagicDamage(Random rnd, double mAtk, double mDef, double power,
        int casterLevel, int magicLevel, int targetLevel, int mCritRate, bool targetIsPlayer = false,
        double magicCritDmg = 1d)
    {
        bool mcrit = RollMCrit(rnd, mCritRate);
        double damage = 91d * Math.Sqrt(Math.Max(0, mAtk)) / Math.Max(1d, mDef) * Math.Max(0, power);
        var outcome = MagicOutcome.Normal;
        if (!RollMagicSuccess(rnd, targetLevel, magicLevel))
        {
            if (RollMagicSuccess(rnd, targetLevel, magicLevel) && targetLevel - casterLevel <= 9)
            {
                damage /= 2;
                outcome = MagicOutcome.HalfFail;
            }
            else
            {
                damage = 1;
                outcome = MagicOutcome.Resisted;
            }
        }
        else if (mcrit)
        {
            damage *= targetIsPlayer ? 2.5 : 3;
            damage *= Math.Max(0, magicCritDmg);
            outcome = MagicOutcome.Critical;
        }
        return (Math.Max(1, (int)damage), outcome);
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
    /// Then weapon random spread and skill crit ×2 (rate = baseCritRate·10·STR bonus ‰).
    /// </summary>
    public static (int Damage, BlowOutcome Outcome) RollBlowDamage(Random rnd, BlowEffect effect, double pAtk, double pDef,
        double power, bool soulshot, bool front, bool behind, int randomPercent, int baseCritRate, double strBonus,
        double shieldRate = 0, int shieldDef = 0, double critDamageMod = 1d, double weaponTraitBonus = 1d)
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
            damage *= 2 * Math.Max(0, critDamageMod);
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
        Math.Max(1, (int)(91d * Math.Sqrt(Math.Max(0, mAtk)) / Math.Max(1d, mDef) * Math.Max(0, power)));

    /// <summary>Mobius <c>Formulas.calcPhysDam</c> core (no crit/shots/random/elements): 70 * pAtk / pDef, min 1.</summary>
    public static int PhysicalDamage(double pAtk, double pDef) =>
        Math.Max(1, (int)(70d * Math.Max(0d, pAtk) / Math.Max(1d, pDef)));
}
