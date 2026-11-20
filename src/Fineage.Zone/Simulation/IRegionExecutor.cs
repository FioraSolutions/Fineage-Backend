namespace Fineage.Zone.Simulation;

/// <summary>
/// Runs one stage's region bodies, indexed in ascending <see cref="RegionId"/> order, and returns once every region
/// has finished (the stage barrier).
/// </summary>
public interface IRegionExecutor
{
    void Run(int regionCount, Action<int> regionBody);
}

/// <summary>Regions one after another in ascending <see cref="RegionId"/> order on the calling thread.</summary>
public sealed class SequentialRegionExecutor : IRegionExecutor
{
    public static SequentialRegionExecutor Instance { get; } = new();

    public void Run(int regionCount, Action<int> regionBody)
    {
        for (int i = 0; i < regionCount; i++)
            regionBody(i);
    }
}

/// <summary>Regions spread across worker threads. Only for stages whose region bodies touch region-owned state alone.</summary>
public sealed class ParallelRegionExecutor : IRegionExecutor
{
    readonly ParallelOptions _options;

    public ParallelRegionExecutor(int maxDegreeOfParallelism)
    {
        if (maxDegreeOfParallelism <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), maxDegreeOfParallelism,
                "Parallel region executor needs at least one worker.");
        _options = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism };
    }

    public void Run(int regionCount, Action<int> regionBody) =>
        Parallel.For(0, regionCount, _options, regionBody);
}
