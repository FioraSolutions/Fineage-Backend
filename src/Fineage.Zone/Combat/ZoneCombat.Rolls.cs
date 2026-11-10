using Fineage.Game;

namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
    public enum ShieldResult { None, Block, Perfect }

    /// <summary>
    /// Mobius <c>Formulas.calcShldUse</c> (no facing-angle check yet): rate = rShld · DEX bonus (×1.3 vs bows);
    /// perfect block when Rnd(100) &gt; 100 − 5, else block when rate &gt; Rnd(100).
    /// </summary>
    public static ShieldResult RollShield(Random rnd, double shieldRate, bool attackerBow)
    {
        if (shieldRate <= 1e-6)
            return ShieldResult.None;
        if (attackerBow)
            shieldRate *= 1.3;
        if (100 - PerfectShieldBlockPercent < rnd.Next(100))
            return ShieldResult.Perfect;
        return shieldRate > rnd.Next(100) ? ShieldResult.Block : ShieldResult.None;
    }

    /// <summary>Mobius <c>Formulas.calcHitMiss</c>: (80 + 2·(acc − eva))·10 ‰, clamped 200..980.</summary>
    public static int HitChancePermille(double accuracy, double evasion) => HitChancePermille(accuracy, evasion, 1d);

    /// <summary>calcHitMiss with HitConditionBonusData multiplier (Java int *= double truncates), then clamp 200..980.</summary>
    public static int HitChancePermille(double accuracy, double evasion, double conditionBonus) =>
        Math.Clamp((int)((int)((80 + 2 * (accuracy - evasion)) * 10) * conditionBonus), 200, 980);

    /// <summary>Mobius <c>Util.calculateHeadingFrom</c>: atan2 angle (0..360) · 182.044444444.</summary>
    public static int HeadingFrom(int fromX, int fromY, int toX, int toY)
    {
        double angle = AngleFrom(fromX, fromY, toX, toY);
        return (int)(angle * 182.044444444);
    }

    /// <summary>Mobius <c>Util.calculateAngleFrom</c>: degrees 0..360 from (x1,y1) towards (x2,y2).</summary>
    public static double AngleFrom(int x1, int y1, int x2, int y2)
    {
        double angle = Math.Atan2(y2 - y1, x2 - x1) * 180 / Math.PI;
        return angle < 0 ? angle + 360 : angle;
    }

    static bool WithinAngle(double angleChar, int heading, double maxDiff = 60)
    {
        double diff = angleChar - heading / 182.044444444;
        if (diff <= -360 + maxDiff) diff += 360;
        if (diff >= 360 - maxDiff) diff -= 360;
        return Math.Abs(diff) <= maxDiff;
    }

    /// <summary>Mobius <c>Creature.isBehind</c>: angle attacker→target within 60° of the target's heading.</summary>
    public static bool IsBehind(int ax, int ay, int tx, int ty, int targetHeading) =>
        WithinAngle(AngleFrom(ax, ay, tx, ty), targetHeading);

    /// <summary>Mobius <c>Creature.isInFrontOf</c>: angle target→attacker within 60° of the target's heading.</summary>
    public static bool IsInFrontOf(int ax, int ay, int tx, int ty, int targetHeading) =>
        WithinAngle(AngleFrom(tx, ty, ax, ay), targetHeading);

    /// <summary>
    /// Mobius <c>HitConditionBonusData.getConditionBonus</c>: 100 + high/low (Δz &gt; ±50) + dark (night) + back/front/side, /100, min 0.
    /// Rain is unsupported in Mobius as well.
    /// </summary>
    public static double HitConditionBonus(int ax, int ay, int az, int tx, int ty, int tz, int targetHeading, bool night = false)
    {
        double mod = 100;
        if (az - tz > 50) mod += HitBonusHigh;
        else if (az - tz < -50) mod += HitBonusLow;
        if (night) mod += HitBonusDark;
        if (IsBehind(ax, ay, tx, ty, targetHeading)) mod += HitBonusBack;
        else if (IsInFrontOf(ax, ay, tx, ty, targetHeading)) mod += HitBonusFront;
        else mod += HitBonusSide;
        return Math.Max(mod / 100, 0);
    }

    /// <summary>Mobius FuncAtkAccuracy / FuncAtkEvasion: base + sqrt(DEX)·6 + level.</summary>
    public static double AccuracyOrEvasion(double baseValue, int dex, int level) =>
        baseValue + Math.Sqrt(Math.Max(0, dex)) * 6 + Math.Max(1, level);

    /// <summary>
    /// One melee swing (Mobius doAttack → calcHitMiss, calcCrit, calcPhysDam): miss → (0, Miss);
    /// crit doubles; damage × (1 + Rnd[−random, random] / 100).
    /// </summary>
    public static (int Damage, byte Flags) RollMelee(Random rnd, double pAtk, double pDef, double accuracy, double evasion,
        int critPermille, int randomPercent) =>
        RollMelee(rnd, pAtk, pDef, accuracy, evasion, critPermille, randomPercent, false, 0, 0, 0, false);

    public static (int Damage, byte Flags) RollMelee(Random rnd, double pAtk, double pDef, double accuracy, double evasion,
        int critPermille, int randomPercent, bool soulshot, int shotGrade, double shieldRate, int shieldDef, bool attackerBow) =>
        RollMelee(rnd, pAtk, pDef, accuracy, evasion, critPermille, randomPercent, soulshot, shotGrade, shieldRate, shieldDef, attackerBow, 1d);

    public static (int Damage, byte Flags) RollMelee(Random rnd, double pAtk, double pDef, double accuracy, double evasion,
        int critPermille, int randomPercent, bool soulshot, int shotGrade, double shieldRate, int shieldDef, bool attackerBow,
        double hitConditionBonus) =>
        RollMelee(rnd, pAtk, pDef, accuracy, evasion, critPermille, randomPercent, soulshot, shotGrade, shieldRate, shieldDef,
            attackerBow, hitConditionBonus, 1d);

    public static (int Damage, byte Flags) RollMelee(Random rnd, double pAtk, double pDef, double accuracy, double evasion,
        int critPermille, int randomPercent, bool soulshot, int shotGrade, double shieldRate, int shieldDef, bool attackerBow,
        double hitConditionBonus, double critDamageMod) =>
        RollMelee(rnd, pAtk, pDef, accuracy, evasion, critPermille, randomPercent, soulshot, shotGrade, shieldRate, shieldDef,
            attackerBow, hitConditionBonus, critDamageMod, 1d);

    /// <summary>
    /// Full swing: miss → shield → crit → calcPhysDam. Soulshot doubles P.Atk (flag 0x10 | grade); shield block
    /// adds sDef to P.Def, perfect block deals 1 (flag 0x40). Crit damage · <paramref name="critDamageMod"/>
    /// (<c>Stat.CRITICAL_DAMAGE</c>, default 1). <paramref name="weaponTraitBonus"/> is Mobius
    /// <c>calcWeaponTraitBonus</c> (default 1).
    /// </summary>
    public static (int Damage, byte Flags) RollMelee(Random rnd, double pAtk, double pDef, double accuracy, double evasion,
        int critPermille, int randomPercent, bool soulshot, int shotGrade, double shieldRate, int shieldDef, bool attackerBow,
        double hitConditionBonus, double critDamageMod, double weaponTraitBonus)
    {
        byte ssFlags = soulshot ? (byte)(HitFlagSoulshot | (shotGrade & 0x0F)) : (byte)0;
        if (rnd.Next(1000) >= HitChancePermille(accuracy, evasion, hitConditionBonus))
            return (0, (byte)(HitFlagMiss | ssFlags));
        var shield = RollShield(rnd, shieldRate, attackerBow);
        byte flags = (byte)(ssFlags | (shield != ShieldResult.None ? HitFlagShield : 0));
        if (shield == ShieldResult.Perfect)
            return (1, flags);
        bool crit = Math.Clamp(critPermille, 0, MaxCritPermille) > rnd.Next(1000);
        if (soulshot)
            pAtk *= 2;
        if (shield == ShieldResult.Block)
            pDef += shieldDef;
        double damage = 70d * Math.Max(0d, pAtk) / Math.Max(1d, pDef);
        if (crit)
            damage *= 2 * Math.Max(0, critDamageMod);
        if (randomPercent > 0)
            damage *= 1 + rnd.Next(-randomPercent, randomPercent + 1) / 100d;
        damage *= Math.Max(0, weaponTraitBonus);
        if (damage <= 0)
            return (0, (byte)(flags | (crit ? HitFlagCrit : 0)));
        return (Math.Max(1, (int)damage), (byte)(flags | (crit ? HitFlagCrit : 0)));
    }

    /// <summary>
    /// Mobius <c>Formulas.calcWeaponTraitBonus</c>: <c>1 − (defenceTraits[weapon] − 1)</c>.
    /// <paramref name="defenceTraits"/> keys are Fineage tokens (<c>sword</c>, <c>bow</c>, …) from
    /// <c>npcs.json</c> / <see cref="Npcs.NpcTemplate.DefenceTraits"/>. Missing → 1.
    /// Fist weapons prefer <c>dualfist</c> (Mobius DualFist gloves) then <c>fist</c>.
    /// </summary>
}
