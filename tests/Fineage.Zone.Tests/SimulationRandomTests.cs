using Fineage.Zone.Simulation;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SimulationRandomTests
{
    static readonly RegionId Elf = new(0, 21, 19);

    /// <summary>Reference xoshiro256** seeded through SplitMix64, computed outside .NET: a runtime upgrade cannot change a replay.</summary>
    [Theory]
    [InlineData(0UL, 0x99ec5f36cb75f2b4UL, 0xbf6e1f784956452aUL, 0x1a5f849d4933e6e0UL)]
    [InlineData(42UL, 0x15780b2e0c2ec716UL, 0x6104d9866d113a7eUL, 0xae17533239e499a1UL)]
    public void Stream_matches_the_reference_generator(ulong seed, ulong first, ulong second, ulong third)
    {
        var random = new SimulationRandom(seed);
        Assert.Equal([first, second, third], new[] { random.NextUInt64(), random.NextUInt64(), random.NextUInt64() });
    }

    [Fact]
    public void Stream_seeds_are_fixed_functions_of_world_seed_purpose_and_region()
    {
        Assert.Equal(0x901883d2427428f4UL, SimulationRandom.StreamSeed(42, RandomPurpose.Combat));
        Assert.Equal(0x12590d369b2b8db5UL, SimulationRandom.StreamSeed(42, RandomPurpose.Combat, Elf));
        Assert.Equal(0x2051d65c45f83c53UL, SimulationRandom.StreamSeed(42, RandomPurpose.Combat, new RegionId(3, -1, -2)));
    }

    [Fact]
    public void Every_purpose_and_region_gets_its_own_stream()
    {
        var seeds = new HashSet<ulong>();
        foreach (var purpose in Enum.GetValues<RandomPurpose>())
        {
            Assert.True(seeds.Add(SimulationRandom.StreamSeed(7, purpose)));
            for (int x = -2; x <= 2; x++)
                for (int y = -2; y <= 2; y++)
                    for (int instance = 0; instance <= 1; instance++)
                        Assert.True(seeds.Add(SimulationRandom.StreamSeed(7, purpose, new RegionId(instance, x, y))));
        }

        Assert.NotEqual(SimulationRandom.StreamSeed(7, RandomPurpose.Drop, Elf), SimulationRandom.StreamSeed(8, RandomPurpose.Drop, Elf));
    }

    [Fact]
    public void A_region_stream_advances_only_by_its_own_draws()
    {
        var alone = new SeededRegionRandom(99, RandomPurpose.Spawn);
        var mixed = new SeededRegionRandom(99, RandomPurpose.Spawn);
        var other = new RegionId(0, 22, 19);

        var expected = Enumerable.Range(0, 20).Select(_ => alone.For(Elf).Next(1000)).ToList();
        var actual = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            mixed.For(other).NextDouble();
            actual.Add(mixed.For(Elf).Next(1000));
        }

        Assert.Equal(expected, actual);
        Assert.Same(mixed.For(Elf), mixed.For(Elf));
    }

    [Fact]
    public void Ranges_hold_their_bounds_including_the_extremes()
    {
        var random = new SimulationRandom(3);
        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(random.Next(), 0, int.MaxValue - 1);
            Assert.InRange(random.Next(7), 0, 6);
            Assert.InRange(random.Next(-5, 5), -5, 4);
            Assert.InRange(random.Next(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
            Assert.InRange(random.NextInt64(long.MinValue, long.MaxValue), long.MinValue, long.MaxValue - 1);
            Assert.InRange(random.NextDouble(), 0.0, Math.BitDecrement(1.0));
            Assert.InRange(random.NextSingle(), 0f, MathF.BitDecrement(1f));
        }

        Assert.Equal(0, random.Next(0));
        Assert.Equal(4, random.Next(4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(2, 1));
    }

    [Fact]
    public void Draws_are_uniform_enough_for_percent_chances()
    {
        var random = new SimulationRandom(2026);
        var counts = new int[100];
        const int draws = 200_000;
        for (int i = 0; i < draws; i++)
            counts[random.Next(100)]++;

        // Chi-square, 99 degrees of freedom: the 99.9th percentile is about 148.
        double expected = draws / 100.0;
        double chi = counts.Sum(c => (c - expected) * (c - expected) / expected);
        Assert.InRange(chi, 0, 148);

        double mean = Enumerable.Range(0, draws).Average(_ => random.NextDouble());
        Assert.InRange(mean, 0.495, 0.505);
    }

    [Fact]
    public void NextBytes_fills_little_endian_words_and_a_tail()
    {
        var bytes = new byte[11];
        new SimulationRandom(42).NextBytes(bytes);
        var words = new SimulationRandom(42);
        ulong first = words.NextUInt64();
        ulong second = words.NextUInt64();

        Assert.Equal(BitConverter.GetBytes(first), bytes[..8]);
        Assert.Equal(BitConverter.GetBytes(second)[..3], bytes[8..]);
    }

    [Theory]
    [InlineData("0", 0UL)]
    [InlineData("18446744073709551615", ulong.MaxValue)]
    [InlineData(" 42 ", 42UL)]
    public void Seed_setting_parses_unsigned_64_bit_decimals(string text, ulong seed) =>
        Assert.Equal(seed, ZoneSimulationSettings.ParseSeed(text));

    [Theory]
    [InlineData("-1")]
    [InlineData("0x10")]
    [InlineData("18446744073709551616")]
    [InlineData("seed")]
    public void Invalid_seed_setting_names_the_variable(string text)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ZoneSimulationSettings.ParseSeed(text));
        Assert.Contains("FINEAGE_ZONE_SEED", error.Message);
    }

    [Fact]
    public void Unset_seed_setting_draws_a_fresh_seed()
    {
        var seeds = Enumerable.Range(0, 4).Select(_ => ZoneSimulationSettings.ParseSeed(null)).ToHashSet();
        Assert.True(seeds.Count > 1);
        ZoneSimulationSettings.ParseSeed("");
    }
}
