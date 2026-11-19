namespace Fineage.Game;

/// <summary>Combat stats a buff/debuff function can touch (Mobius Stat names in parentheses).</summary>
public enum CombatStat
{
    PAtk,          // pAtk
    PDef,          // pDef
    MAtk,          // mAtk
    MDef,          // mDef
    PAtkSpd,       // pAtkSpd
    MAtkSpd,       // mAtkSpd
    Accuracy,      // accCombat
    Evasion,       // rEvas
    CritRate,      // critRate
    RunSpd,        // runSpd = Stat.MOVE_SPEED
    RegHp,         // regHp
    RegMp,         // regMp
    WeightLimit,   // weightLimit
    WeightPenalty, // weightPenalty
    MCritRate,     // mCritRate
    MagicCritDmg,  // mCritPower
    CritDmg,       // critDmg
    CritDmgAdd,    // critDmgAdd (flat, Vicious Stance / Critical Power / SA)
    CritRatePos,   // critRatePos (Focus Chance 356: multiplier on the plain-attack crit rate, by attack side)
    CritDmgPos,    // critDmgPos (Focus Power 357 / Focus Death 355: multiplier on crit damage, by attack side)
    BlowRate,      // blowRate
    PSkillEvasion, // pSkillEvas
    MaxHp,         // maxHp
    MaxMp,         // maxMp
    MaxCp,         // maxCp
    HealEffect,    // healEffect
    PReuse,        // pReuse
    MReuse,        // mReuse
    DReuse,        // dReuse
    SDef,          // sDef
    RShld,         // rShld
    ReflectDam,    // reflectDam
    ReflectSkillPhysic, // reflectSkillPhysic
    ReflectSkillMagic,  // reflectSkillMagic
    VengeancePdam, // vengeancePdam
    VengeanceMdam, // vengeanceMdam
    AbsorbDam,     // absorbDam
    PhysicalMpConsumeRate, // physicalMpConsumeRate
    MagicalMpConsumeRate,  // magicalMpConsumeRate
    DanceMpConsumeRate,    // danceMpConsumeRate
    ShieldDefAngle,        // shieldDefAngle
    SkillCritical,         // skillCritical
    SkillCriticalProbability, // skillCriticalProbability
    AtkCountMax,   // atkCountMax
    PAtkRange,     // pAtkRange
    Breath,        // breath
    Fall,          // fall
    CancelVuln,    // cancelVuln
    DebuffVuln,    // debuffVuln
    HolyPower,     // holyPower
    DarkRes,       // darkRes
    HolyRes,       // holyRes
    FireRes,       // fireRes
    WaterRes,      // waterRes
    WindRes,       // windRes
    EarthRes,      // earthRes
    TransDam,      // transDam (Transfer Pain: percent of incoming damage sent to the servitor)
    Cancel         // cancel / ATTACK_CANCEL (Concentration: folded onto calcAtkBreak init)
}

/// <summary>
/// Attacker's side relative to the target for Mobius positional funcs
/// (<c>&lt;player front="true"/&gt;</c>, <c>&lt;player behind="true"/&gt;</c>, and the <c>and</c> of both negated = side).
/// </summary>
public enum SkillPosition
{
    /// <summary>No target yet / no positional condition — positional funcs are skipped.</summary>
    None = 0,
    Front,
    Side,
    Behind
}

/// <param name="Using">Mobius <c>&lt;using kind&gt;</c> condition (upper-case kinds); null = unconditional.</param>
/// <param name="Resting">
/// Mobius <c>&lt;player resting="true"/&gt;</c> condition (Vital Force 148 HP/MP regen): applies only while seated.
/// </param>
/// <param name="Position">
/// Mobius <c>&lt;player front|behind&gt;</c> condition (Focus Chance 356 crit rate, Focus Power 357 crit damage):
/// the func applies only when the owner attacks from that side; <see cref="SkillPosition.None"/> = unconditional.
/// </param>
/// <param name="HpBelowPercent">
/// Mobius <c>&lt;player hp="30"/&gt;</c> (Final Frenzy / Final Fortress): the func applies only when current HP
/// is at or below this percent. Null = unconditional.
/// </param>
public sealed record SkillStatFunc(
    CombatStat Stat,
    char Op,
    double[] Values,
    string[]? Using = null,
    bool Resting = false,
    SkillPosition Position = SkillPosition.None,
    int? HpBelowPercent = null,
    bool Moving = false,
    bool Standing = false,
    string[]? NotUsing = null)
{
    public double ValueAt(int level) => Values.Length == 0 ? 0 : Values[Math.Clamp(level, 1, Values.Length) - 1];
}

public sealed record SkillStatEffect(int SkillId, string Name, int Levels, IReadOnlyList<SkillStatFunc> Funcs)
{
    /// <summary>
    /// Mobius skill-level <c>&lt;cond&gt;&lt;using kind&gt;</c> (Focus Chance 356 / Focus Power 357 need a dagger).
    /// Mobius refuses the cast with SysMsg 113 when the owner does not wear one; null = no gate.
    /// </summary>
    public string[]? Using { get; init; }
}

/// <summary>
/// Accumulated stat functions from active abnormals / passives. Mobius order
/// (<c>StatFunction.java:24-31</c>): <c>SET</c> → <c>MUL</c> → <c>ADD</c>/<c>SUB</c>.
/// </summary>
public sealed class CombatStatMods
{
    readonly double[] _mul = Enumerable.Repeat(1d, Enum.GetValues<CombatStat>().Length).ToArray();
    readonly double[] _add = new double[Enum.GetValues<CombatStat>().Length];
    readonly double?[] _set = new double?[Enum.GetValues<CombatStat>().Length];

    public static readonly CombatStatMods None = new();

    public bool IsEmpty { get; private set; } = true;

    public void Add(SkillStatFunc func, int level)
    {
        int i = (int)func.Stat;
        double v = func.ValueAt(level);
        switch (func.Op)
        {
            case 'm': _mul[i] *= v; break;
            case 'a': _add[i] += v; break;
            case 's': _add[i] -= v; break;
            case '=': _set[i] = v; break;
            default: return;
        }
        IsEmpty = false;
    }

    /// <summary>
    /// Folds one stat the way Mobius' <c>Calculator</c> does: <c>SET</c> replaces the value, then the product of all
    /// <c>MUL</c>s, then the sum of signed <c>ADD</c>/<c>SUB</c>s.
    /// </summary>
    public double Apply(CombatStat stat, double value)
    {
        int i = (int)stat;
        return (_set[i] ?? value) * _mul[i] + _add[i];
    }

    public int Apply(CombatStat stat, int value) => (int)Apply(stat, (double)value);
}
