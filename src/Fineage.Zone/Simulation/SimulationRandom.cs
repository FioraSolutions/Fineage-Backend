namespace Fineage.Zone.Simulation;

/// <summary>What a random stream is used for. Values are part of the stream seed: never renumber them.</summary>
public enum RandomPurpose
{
    Combat = 1,
    Spawn = 2,
    Drop = 3,
    Death = 4,
    Service = 5,
    Quest = 6,
    SoulCrystal = 7,
    Fishing = 8,
    Manor = 9,
    Lottery = 10,
    Mammon = 11,
}

/// <summary>
/// xoshiro256** seeded through SplitMix64. The algorithm is owned here rather than taken from <see cref="Random"/>, so a
/// seed replays to the same rolls on every runtime version. Not thread-safe; a stream belongs to one region (or one
/// global system) at a time.
/// </summary>
public sealed class SimulationRandom : Random
{
    ulong _s0, _s1, _s2, _s3;

    public SimulationRandom(ulong seed)
    {
        // SplitMix64 is a bijection on its counter, so four consecutive outputs are never all zero.
        _s0 = SplitMix(ref seed);
        _s1 = SplitMix(ref seed);
        _s2 = SplitMix(ref seed);
        _s3 = SplitMix(ref seed);
    }

    /// <summary>The seed of one global system's stream (<paramref name="purpose"/>) in a world seeded with <paramref name="worldSeed"/>.</summary>
    public static ulong StreamSeed(ulong worldSeed, RandomPurpose purpose) => Mix(Mix(worldSeed) ^ (ulong)purpose);

    /// <summary>The seed of one region's stream for <paramref name="purpose"/>.</summary>
    public static ulong StreamSeed(ulong worldSeed, RandomPurpose purpose, RegionId region)
    {
        ulong h = StreamSeed(worldSeed, purpose);
        h = Mix(h ^ (uint)region.InstanceId);
        h = Mix(h ^ (uint)region.X);
        return Mix(h ^ (uint)region.Y);
    }

    public ulong NextUInt64()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);
        return result;
    }

    public override int Next()
    {
        while (true)
        {
            int value = (int)(NextUInt64() >> 33);
            if (value != int.MaxValue)
                return value;
        }
    }

    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (int)Below((ulong)maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return (int)(minValue + (long)Below((ulong)((long)maxValue - minValue)));
    }

    public override long NextInt64()
    {
        while (true)
        {
            long value = (long)(NextUInt64() >> 1);
            if (value != long.MaxValue)
                return value;
        }
    }

    public override long NextInt64(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (long)Below((ulong)maxValue);
    }

    public override long NextInt64(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return minValue + (long)Below((ulong)(maxValue - minValue));
    }

    public override double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public override float NextSingle() => (NextUInt64() >> 40) * (1f / (1 << 24));

    public override void NextBytes(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        NextBytes(buffer.AsSpan());
    }

    public override void NextBytes(Span<byte> buffer)
    {
        while (buffer.Length > 0)
        {
            ulong value = NextUInt64();
            int count = Math.Min(sizeof(ulong), buffer.Length);
            for (int i = 0; i < count; i++)
                buffer[i] = (byte)(value >> (8 * i));
            buffer = buffer[count..];
        }
    }

    protected override double Sample() => NextDouble();

    /// <summary>Uniform in [0, <paramref name="bound"/>) by Lemire's multiply-and-reject; 0 when the bound is 0.</summary>
    ulong Below(ulong bound)
    {
        ulong high = Math.BigMul(NextUInt64(), bound, out ulong low);
        if (low < bound)
        {
            ulong threshold = (0 - bound) % bound;
            while (low < threshold)
                high = Math.BigMul(NextUInt64(), bound, out low);
        }

        return high;
    }

    static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

    static ulong SplitMix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        return Mix(state);
    }

    static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
