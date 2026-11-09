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
    BlowRate,      // blowRate
    PSkillEvasion  // pSkillEvas
}

/// <param name="Using">Mobius <c>&lt;using kind&gt;</c> condition (upper-case kinds); null = unconditional.</param>
public sealed record SkillStatFunc(CombatStat Stat, char Op, double[] Values, string[]? Using = null)
{
    public double ValueAt(int level) => Values.Length == 0 ? 0 : Values[Math.Clamp(level, 1, Values.Length) - 1];
}

public sealed record SkillStatEffect(int SkillId, string Name, int Levels, IReadOnlyList<SkillStatFunc> Funcs);

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
