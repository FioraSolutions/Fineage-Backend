namespace Fineage.Zone.Simulation;

/// <summary>The random stream a rule draws from, chosen by the region that owns the acting entity.</summary>
public interface IRegionRandom
{
    Random For(RegionId region);
}

/// <summary>
/// One <see cref="SimulationRandom"/> per region for one <see cref="RandomPurpose"/>, seeded by
/// (world seed, purpose, region). A region's stream advances only by the draws made for that region, so regions stepped
/// in parallel or in another order roll the same values, and a new draw in one system never shifts another's sequence.
/// </summary>
public sealed class SeededRegionRandom : IRegionRandom
{
    readonly Dictionary<RegionId, SimulationRandom> _streams = [];
    readonly ulong _worldSeed;
    readonly RandomPurpose _purpose;

    public SeededRegionRandom(ulong worldSeed, RandomPurpose purpose)
    {
        _worldSeed = worldSeed;
        _purpose = purpose;
    }

    public Random For(RegionId region)
    {
        lock (_streams)
        {
            if (!_streams.TryGetValue(region, out var stream))
            {
                stream = new SimulationRandom(SimulationRandom.StreamSeed(_worldSeed, _purpose, region));
                _streams[region] = stream;
            }

            return stream;
        }
    }
}

/// <summary>One caller-supplied generator for every region: tests and tools that script outcomes.</summary>
public sealed class SharedRegionRandom(Random random) : IRegionRandom
{
    readonly Random _random = random ?? throw new ArgumentNullException(nameof(random));

    public Random For(RegionId region) => _random;
}
