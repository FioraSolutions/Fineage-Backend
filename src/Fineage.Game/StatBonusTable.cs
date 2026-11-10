namespace Fineage.Game;

public enum StatSlot
{
    Str,
    Dex,
    Con,
    Int,
    Wit,
    Men
}

/// <summary>STR/DEX/CON/INT/WIT/MEN multipliers. Index is the stat value, 0 through 99.</summary>
public sealed class StatBonusTable
{
    readonly double[] _str;
    readonly double[] _dex;
    readonly double[] _con;
    readonly double[] _int;
    readonly double[] _wit;
    readonly double[] _men;

    internal StatBonusTable(double[] str, double[] dex, double[] con, double[] intel, double[] wit, double[] men)
    {
        _str = str;
        _dex = dex;
        _con = con;
        _int = intel;
        _wit = wit;
        _men = men;
    }

    public double Bonus(StatSlot stat, int value)
    {
        var table = TableFor(stat);
        if (value < 0 || value >= table.Length)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Stat value is outside the bonus table.");
        return table[value];
    }

    /// <summary>
    /// Same as <see cref="Bonus"/> but clamps to the table instead of throwing.
    /// <paramref name="clamped"/> is true when the raw value was outside 0..Length-1 (henna overflow, bad data).
    /// </summary>
    public double BonusClamped(StatSlot stat, int value, out bool clamped)
    {
        var table = TableFor(stat);
        clamped = value < 0 || value >= table.Length;
        int idx = clamped ? Math.Clamp(value, 0, table.Length - 1) : value;
        return table[idx];
    }

    double[] TableFor(StatSlot stat) =>
        stat switch
        {
            StatSlot.Str => _str,
            StatSlot.Dex => _dex,
            StatSlot.Con => _con,
            StatSlot.Int => _int,
            StatSlot.Wit => _wit,
            StatSlot.Men => _men,
            _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "Unknown stat.")
        };
}

/// <summary>Experience required to be each level. <see cref="ExpToBeLevel"/> is cumulative.</summary>
