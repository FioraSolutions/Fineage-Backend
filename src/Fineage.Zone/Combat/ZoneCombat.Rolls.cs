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
    public static double WeaponTraitBonus(WeaponType weapon, IReadOnlyDictionary<string, double>? defenceTraits)
    {
        if (defenceTraits == null || defenceTraits.Count == 0 || weapon == WeaponType.None)
            return 1d;
        string? key = weapon switch
        {
            WeaponType.Sword => "sword",
            WeaponType.Blunt => "blunt",
            WeaponType.Dagger => "dagger",
            WeaponType.Bow => "bow",
            WeaponType.Pole => "pole",
            WeaponType.Dual => "dual",
            WeaponType.Fist => "dualfist",
            WeaponType.Etc => "etc",
            _ => null
        };
        if (key == null)
            return 1d;
        if (!defenceTraits.TryGetValue(key, out var defence) &&
            !(weapon == WeaponType.Fist && defenceTraits.TryGetValue("fist", out defence)) &&
            !(weapon == WeaponType.Dagger && defenceTraits.TryGetValue("dualdagger", out defence)))
            return 1d;
        return 1d - (defence - 1d);
    }

    /// <summary>Mobius <c>TraitType</c> type=2 keys stored in <c>defenceTraits</c> / attack-trait maps.</summary>
    public static readonly string[] RaceWeaknessKeys =
    [
        "bugWeakness", "animalWeakness", "plantWeakness", "beastWeakness",
        "dragonWeakness", "giantWeakness", "constructWeakness", "valakas"
    ];

    /// <summary>
    /// Mobius <c>Formulas.calcGeneralTraitBonus</c> for type=2: both sides must have the trait;
    /// <c>(attack − defence) + 1</c> clamped to [0.05, 2].
    /// </summary>
    public static double GeneralWeaknessBonus(
        IReadOnlyDictionary<string, double>? attackTraits,
        IReadOnlyDictionary<string, double>? defenceTraits,
        string key)
    {
        if (attackTraits is not { Count: > 0 } || defenceTraits is not { Count: > 0 })
            return 1d;
        if (!attackTraits.TryGetValue(key, out var attack) || !defenceTraits.TryGetValue(key, out var defence))
            return 1d;
        return Math.Clamp((attack - defence) + 1d, 0.05, 2d);
    }

    /// <summary>
    /// Mobius <c>Formulas.calcAttackTraitBonus</c>: weapon trait × product of type-2 weakness bonuses.
    /// </summary>
    public static double AttackTraitBonus(
        WeaponType weapon,
        IReadOnlyDictionary<string, double>? defenceTraits,
        IReadOnlyDictionary<string, double>? attackTraits = null)
    {
        double weaponBonus = WeaponTraitBonus(weapon, defenceTraits);
        if (weaponBonus == 0)
            return 0;
        double weakness = 1d;
        foreach (var key in RaceWeaknessKeys)
        {
            weakness *= GeneralWeaknessBonus(attackTraits, defenceTraits, key);
            if (weakness == 0)
                return 0;
        }
        return Math.Clamp(weaponBonus * weakness, 0.05, 2d);
    }

    /// <summary>Mobius <c>Formulas.calcAtkSpd</c> for skills: magic time·333/Cspd, physical time·300/Pspd.</summary>
    public static int SkillTimeMs(int baseMs, bool magic, int mAtkSpd, int pAtkSpd) =>
        baseMs <= 0 ? 0 : (int)(magic ? baseMs * 333d / Math.Max(1, mAtkSpd) : baseMs * 300d / Math.Max(1, pAtkSpd));

    /// <summary>Interlude reuse scaling: reuse·333 / (magic ? Cspd : Pspd).</summary>
    public static int SkillReuseMs(int baseMs, bool magic, int mAtkSpd, int pAtkSpd) =>
        baseMs <= 0 ? 0 : (int)(baseMs * 333d / Math.Max(1, magic ? mAtkSpd : pAtkSpd));

    /// <summary>Interlude <c>calcPhysDam</c> with skill: 70·(pAtk·(ss?2:1) + power) / (pDef [+ sDef]).</summary>
    public static int PhysicalSkillDamage(double pAtk, double pDef, double power, bool soulshot) =>
        PhysicalSkillDamage(pAtk, pDef, power, soulshot, crit: false);

    /// <summary>
    /// Physical skill damage; on crit Mobius <c>PhysicalAttack</c> ·2 then Fineage also folds
    /// <paramref name="critDamageMod"/> (<c>CRITICAL_DAMAGE</c>).
    /// </summary>
    public static int PhysicalSkillDamage(double pAtk, double pDef, double power, bool soulshot, bool crit,
        double critDamageMod = 1d, double weaponTraitBonus = 1d)
    {
        double damage = 70d * (Math.Max(0, pAtk) * (soulshot ? 2 : 1) + Math.Max(0, power)) / Math.Max(1d, pDef);
        if (crit)
            damage *= 2 * Math.Max(0, critDamageMod);
        damage *= Math.Max(0, weaponTraitBonus);
        if (damage <= 0)
            return 0;
        return Math.Max(1, (int)damage);
    }

    /// <summary>Mobius <c>calcPhysicalSkillEvasion</c>: Rnd(100) &lt; target pSkillEvas % (physical non-debuff only).</summary>
    public static bool RollPhysicalSkillEvasion(Random rnd, double pSkillEvasionPercent) =>
        pSkillEvasionPercent > 0 && rnd.Next(100) < pSkillEvasionPercent;

    /// <summary>Mobius <c>calcCrit</c> for a skill: baseCritRate · 10 · STR bonus ‰.</summary>
    public static bool RollSkillCrit(Random rnd, int baseCritRate, double strBonus) =>
        baseCritRate > 0 && baseCritRate * 10 * strBonus > rnd.Next(1000);

    /// <summary>
    /// Mobius <c>CreatureStat.getMCriticalHit</c> / <c>FuncMAtkCritical</c> (WIT only when armed), then skill
    /// <c>mCritRate</c> set→mul→add folded over that base, truncated and ·10 for ‰. Unarmed with no buffs stays 10 ‰.
    /// </summary>
    public static int PlayerMCritRate(bool armed, double witBonus, CombatStatMods? mods = null)
    {
        double rate = PlayerCombatFormulas.MagicCriticalBase(armed, witBonus);
        if (mods != null && !ReferenceEquals(mods, CombatStatMods.None))
            rate = mods.Apply(CombatStat.MCritRate, rate);
        return (int)rate * 10;
    }

    /// <summary>Mobius <c>calcMCrit</c>: rate ‰ &gt; Rnd(1000).</summary>
    public static bool RollMCrit(Random rnd, int rate) => rate > rnd.Next(1000);

    /// <summary>
    /// Mobius <c>calcMagicSuccess</c> (MAGIC_SUCCESS_RES 1): rate = 100 − round(1.3^(targetLvl − magicLvl)); success when Rnd(100) &lt; rate.
    /// </summary>
    public static bool RollMagicSuccess(Random rnd, int targetLevel, int magicLevel) =>
        rnd.Next(100) < 100 - (int)Math.Round((float)Math.Pow(1.3, targetLevel - magicLevel), MidpointRounding.AwayFromZero);

    public enum MagicOutcome { Normal, Critical, HalfFail, Resisted }

    /// <summary>
    /// Mobius <c>calcMagicDam</c> failure / crit branch for a player caster: failed first roll → second roll succeeds and
    /// level gap ≤ 9 → half damage ("Your attack has failed"), else resisted → 1 damage; otherwise mcrit ×3 vs NPC (2.5 PvP)
    /// then <c>Stat.MAGIC_CRIT_DMG</c> (<paramref name="magicCritDmg"/>, default 1).
    /// </summary>
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
