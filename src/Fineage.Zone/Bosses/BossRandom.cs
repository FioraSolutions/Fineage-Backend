namespace Fineage.Zone.Bosses;

/// <summary>Inclusive draw matching Mobius <c>Rnd.get(origin, bound)</c>.</summary>
public interface IBossRandom
{
    /// <summary>
    /// When <paramref name="minInclusive"/> is greater than or equal to <paramref name="maxInclusive"/>,
    /// the result is <paramref name="minInclusive"/>.
    /// </summary>
    long NextInclusive(long minInclusive, long maxInclusive);
}

public sealed class InclusiveBossRandom : IBossRandom
{
    readonly Random _random;

    public InclusiveBossRandom(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public long NextInclusive(long minInclusive, long maxInclusive)
    {
        if (minInclusive >= maxInclusive)
            return minInclusive;

        ulong width = (ulong)(maxInclusive - minInclusive);
        if (width >= int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInclusive),
                "Boss respawn window does not fit an inclusive draw.");
        }

        int span = (int)width + 1;
        return minInclusive + _random.Next(span);
    }
}
