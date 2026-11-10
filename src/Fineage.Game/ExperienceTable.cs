namespace Fineage.Game;

public sealed class ExperienceTable
{
    readonly long[] _toLevel;

    internal ExperienceTable(int maxLevel, int maxPetLevel, long[] toLevel)
    {
        MaxLevel = maxLevel;
        MaxPetLevel = maxPetLevel;
        _toLevel = toLevel;
    }

    public int MaxLevel { get; }

    public int MaxPetLevel { get; }

    public int HighestRow => _toLevel.Length - 1;

    public long ExpToBeLevel(int level)
    {
        if (level < 1 || level >= _toLevel.Length)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Level must be 1..{HighestRow}.");
        return _toLevel[level];
    }

    /// <summary>
    /// Adds experience and SP. Level is the highest row whose cumulative exp is still &lt;= the new total,
    /// capped at <see cref="MaxLevel"/>. Exp does not grow past the max-level threshold.
    /// </summary>
    public LevelProgress Add(long exp, int level, int sp, long addExp, int addSp)
    {
        if (addExp < 0)
            throw new ArgumentOutOfRangeException(nameof(addExp));
        if (addSp < 0)
            throw new ArgumentOutOfRangeException(nameof(addSp));

        long nextExp = exp < 0 ? 0 : exp;
        if (addExp > 0)
            nextExp = addExp > long.MaxValue - nextExp ? long.MaxValue : nextExp + addExp;

        int cap = Math.Min(MaxLevel, HighestRow);
        if (cap >= 1 && nextExp > _toLevel[cap])
            nextExp = _toLevel[cap];

        int nextLevel = level < 1 ? 1 : level;
        if (addExp > 0)
        {
            nextLevel = 1;
            for (int candidate = 2; candidate <= cap; candidate++)
            {
                if (nextExp < _toLevel[candidate])
                    break;
                nextLevel = candidate;
            }
        }

        int nextSp = sp < 0 ? 0 : sp;
        if (addSp > 0)
            nextSp = addSp > int.MaxValue - nextSp ? int.MaxValue : nextSp + addSp;

        return new LevelProgress(nextExp, nextLevel, nextSp);
    }
}

/// <summary>Experience, level, and SP after a quest reward.</summary>
public readonly record struct LevelProgress(long Exp, int Level, int Sp);

/// <summary>
/// Interlude classes, experience, and stat bonuses loaded from the datapack.
/// Query by class id. Creation is limited to classes whose parent is absent.
/// </summary>
