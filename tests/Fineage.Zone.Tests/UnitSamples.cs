namespace Fineage.Zone.Tests;

/// <summary>A <see cref="Random"/> whose <see cref="NextDouble"/> returns the given samples in order, then throws.</summary>
sealed class UnitSamples : Random
{
    readonly double[] _values;
    int _index;

    public UnitSamples(params double[] values)
    {
        if (values.Length == 0)
            throw new ArgumentException("A sample sequence needs at least one sample.", nameof(values));
        foreach (var value in values)
        {
            if (value < 0 || value >= 1)
                throw new ArgumentOutOfRangeException(nameof(values), value, "Samples must be in [0, 1).");
        }

        _values = values;
    }

    public override double NextDouble()
    {
        if (_index >= _values.Length)
            throw new InvalidOperationException("The sample sequence is exhausted.");
        return _values[_index++];
    }

    protected override double Sample() => NextDouble();
}
