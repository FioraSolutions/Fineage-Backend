namespace Fineage.Game;

/// <summary>
/// Source of the unit interval used by an enchant roll.
/// Mobius draws <c>Rnd.nextDouble()</c> in [0, 1) and compares <c>100 * sample</c> with the chance.
/// </summary>
public interface IEnchantRandom
{
    /// <summary>A value in [0, 1).</summary>
    double NextUnitInterval();
}

/// <summary>Deterministic sequence. Tests pass the samples they want the roll to see.</summary>
public sealed class SequenceEnchantRandom : IEnchantRandom
{
    readonly double[] _values;
    int _index;

    public SequenceEnchantRandom(params double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("An enchant sequence needs at least one sample.", nameof(values));
        foreach (var value in values)
        {
            if (value < 0 || value >= 1)
                throw new ArgumentOutOfRangeException(nameof(values), value, "Samples must be in [0, 1).");
        }

        _values = values;
    }

    public double NextUnitInterval()
    {
        if (_index >= _values.Length)
            throw new InvalidOperationException("Enchant random sequence is exhausted.");
        return _values[_index++];
    }
}

/// <summary><see cref="Random"/> seeded by the caller. The same seed replays the same rolls.</summary>
public sealed class SeededEnchantRandom : IEnchantRandom
{
    readonly Random _random;

    public SeededEnchantRandom(int seed) => _random = new Random(seed);

    public double NextUnitInterval() => _random.NextDouble();
}

/// <summary>Process-wide random. The zone uses this unless a test passes another source.</summary>
public sealed class SharedEnchantRandom : IEnchantRandom
{
    public static readonly SharedEnchantRandom Instance = new();

    public double NextUnitInterval() => Random.Shared.NextDouble();
}
