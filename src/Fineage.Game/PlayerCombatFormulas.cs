namespace Fineage.Game;

/// <summary>
/// Mobius base combat formulas after template / henna / gear+enchant and before buff/passive SET→MUL→ADD.
/// Kept pure so Zone orchestration and unit tests share one SoT without Calculator big-bang.
/// </summary>
public static class PlayerCombatFormulas
{
    /// <summary>Mobius levelMod = (level + 89) / 100.</summary>
    public static double LevelMod(int level) => PlayerGearBases.LevelMod(level);

    public static int PhysicalAttack(double gearPAtk, double strBonus, double levelMod) =>
        (int)(gearPAtk * strBonus * levelMod);

    public static int MagicalAttack(double gearMAtk, double intBonus, double levelMod) =>
        (int)(gearMAtk * intBonus * intBonus * levelMod * levelMod);

    public static int PhysicalDefence(double gearPDef, double levelMod) =>
        Math.Max(1, (int)(gearPDef * levelMod));

    public static int MagicalDefence(double gearMDef, double menBonus, double levelMod) =>
        Math.Max(1, (int)(gearMDef * menBonus * levelMod));

    public static int PhysicalAttackSpeed(double gearPAtkSpd, double dexBonus) =>
        Math.Max(1, (int)(gearPAtkSpd * dexBonus));

    public static int MagicalAttackSpeed(double templateMAtkSpd, double witBonus) =>
        Math.Max(1, (int)(templateMAtkSpd * witBonus));

    /// <summary>Mobius <c>FuncAtkAccuracy</c> with base 0 (players).</summary>
    public static int Accuracy(int dex, int level) =>
        (int)(Math.Sqrt(Math.Max(0, dex)) * 6 + Math.Max(1, level));

    /// <summary>Mobius critRate · DEX bonus · 10 (permille before buff folds).</summary>
    public static int CriticalRate(int gearCritRate, double dexBonus) =>
        (int)(gearCritRate * dexBonus * 10);

    /// <summary>Mobius <c>FuncAtkEvasion</c> base 0, minus optional shield evasion penalty.</summary>
    public static int Evasion(int dex, int level, int shieldEvasionPenalty = 0) =>
        Accuracy(dex, level) - shieldEvasionPenalty;

    public static double ShieldBlockRate(double shieldRate, double dexBonus, bool shielded) =>
        shielded ? shieldRate * dexBonus : 0;

    /// <summary>
    /// Mobius <c>FuncMAtkCritical</c> base before skill-stat folds (not yet ·10 ‰).
    /// Armed → WIT bonus; unarmed → 1.
    /// </summary>
    public static double MagicCriticalBase(bool armed, double witBonus) =>
        armed ? witBonus : 1d;
}

/// <summary>
/// Pre-mod player combat numbers: formula layer output that Zone folds with <c>CombatStatMods</c>.
/// </summary>
public readonly record struct PlayerFormulaBases(
    int PAtk,
    int MAtk,
    int PDef,
    int MDef,
    int PAtkSpd,
    int MAtkSpd,
    int Accuracy,
    int CritRate,
    int Evasion,
    double ShieldRate,
    double MagicCritBase)
{
    public static PlayerFormulaBases From(
        PlayerEffectiveAttributes attrs,
        PlayerGearBases gear,
        double templateMAtkSpd,
        int level)
    {
        double levelMod = PlayerCombatFormulas.LevelMod(level);
        int shieldPenalty = gear.Shielded ? gear.ShieldEvasionPenalty : 0;
        return new PlayerFormulaBases(
            PlayerCombatFormulas.PhysicalAttack(gear.PAtk, attrs.StrBonus, levelMod),
            PlayerCombatFormulas.MagicalAttack(gear.MAtk, attrs.IntBonus, levelMod),
            PlayerCombatFormulas.PhysicalDefence(gear.PDef, levelMod),
            PlayerCombatFormulas.MagicalDefence(gear.MDef, attrs.MenBonus, levelMod),
            PlayerCombatFormulas.PhysicalAttackSpeed(gear.PAtkSpd, attrs.DexBonus),
            PlayerCombatFormulas.MagicalAttackSpeed(templateMAtkSpd, attrs.WitBonus),
            PlayerCombatFormulas.Accuracy(attrs.Dex, level),
            PlayerCombatFormulas.CriticalRate(gear.CritRate, attrs.DexBonus),
            PlayerCombatFormulas.Evasion(attrs.Dex, level, shieldPenalty),
            PlayerCombatFormulas.ShieldBlockRate(gear.ShieldRate, attrs.DexBonus, gear.Shielded),
            PlayerCombatFormulas.MagicCriticalBase(gear.Armed, attrs.WitBonus));
    }
}

/// <summary>
/// Skill id → level map fed into <c>skill-passives.json</c>.
/// Higher level wins on id collision (enchanted learned skill beats clan copy, etc.).
/// </summary>
public static class PlayerPassiveSources
{
    public static Dictionary<int, int> Merge(
        IReadOnlyDictionary<int, int> knownSkills,
        IReadOnlyDictionary<int, int> clanSkills)
    {
        var map = new Dictionary<int, int>(knownSkills.Count + clanSkills.Count);
        foreach (var (id, level) in knownSkills)
            map[id] = level;
        foreach (var (id, level) in clanSkills)
        {
            if (!map.TryGetValue(id, out int have) || level > have)
                map[id] = level;
        }
        return map;
    }
}
