using Fineage.Game;

namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
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
    /// Mobius <c>Formulas.calcGeneralTraitBonus</c> for type=3 (POISON/HOLD/BLEED/SHOCK/DERANGEMENT/…):
    /// <c>(attackTrait − defenceTrait) + 1</c> clamped to [0.05, 2]. Missing side defaults to 1.
    /// Jewelry <c>DefenceTrait</c> passives land here (Queen Ant poison/hold, Antharas bleed/shock, …).
    /// </summary>
    public static double GeneralResistanceBonus(
        IReadOnlyDictionary<string, double>? attackTraits,
        IReadOnlyDictionary<string, double>? defenceTraits,
        string trait)
    {
        if (string.IsNullOrWhiteSpace(trait))
            return 1d;
        string key = trait.Trim().ToLowerInvariant();
        // Mobius BLEED trait vs jewellery "bleed" DefenceTrait key.
        if (key is "bleeding")
            key = "bleed";
        double attack = 1d;
        double defence = 1d;
        attackTraits?.TryGetValue(key, out attack);
        if (attack <= 0)
            attack = 1d;
        defenceTraits?.TryGetValue(key, out defence);
        if (defence <= 0)
            defence = 1d;
        return Math.Clamp((attack - defence) + 1d, 0.05, 2d);
    }

    /// <summary>Mobius Character.ini defaults for abnormal land clamp.</summary>
    public const int MinAbnormalSuccessRate = 10;
    public const int MaxAbnormalSuccessRate = 90;

    /// <summary>
    /// Mobius <c>Formulas.calcEffectSuccess</c> land chance (0..100) before the Rnd roll.
    /// <paramref name="activateRate"/> −1 or empty <paramref name="basicProperty"/> → 100 (always).
    /// </summary>
    public static double DebuffLandChance(
        int activateRate,
        string? basicProperty,
        int magicLevel,
        int lvlBonusRate,
        int targetLevel,
        int targetBaseStat,
        double traitMod,
        double mAtkMod = 1d,
        double elementMod = 1d,
        double buffDebuffMod = 1d,
        int minChance = MinAbnormalSuccessRate,
        int maxChance = MaxAbnormalSuccessRate)
    {
        if (activateRate < 0 || string.IsNullOrWhiteSpace(basicProperty))
            return 100d;
        int ml = magicLevel <= 0 ? targetLevel + 3 : magicLevel;
        int bonus = Math.Max(1, lvlBonusRate);
        double baseMod = (((((ml - targetLevel) + 3) * bonus) + activateRate) + 30.0) - targetBaseStat;
        double rate = baseMod * elementMod * traitMod * mAtkMod * buffDebuffMod;
        if (traitMod <= 0)
            return 0d;
        return Math.Clamp(rate, minChance, maxChance);
    }

    /// <summary>Mobius <c>calcEffectSuccess</c> Rnd: success when <c>Rnd(100) &lt; chance</c>.</summary>
    public static bool RollDebuffLand(Random rnd, double chancePercent) =>
        rnd.Next(100) < chancePercent;

    /// <summary>
    /// Mobius <c>Formulas.calcCancelSuccess</c>: rate × (1 + (cancelMl − buffMl)/100) clamped to abnormal min/max.
    /// </summary>
    public static bool RollCancelSuccess(
        Random rnd,
        int rate,
        int cancelMagicLevel,
        int buffMagicLevel,
        int minChance = MinAbnormalSuccessRate,
        int maxChance = MaxAbnormalSuccessRate)
    {
        double chance = rate * (buffMagicLevel > 0
            ? 1d + (cancelMagicLevel - buffMagicLevel) / 100d
            : 1d);
        int clamped = (int)Math.Clamp(chance, minChance, maxChance);
        return rnd.Next(100) < clamped;
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
}
