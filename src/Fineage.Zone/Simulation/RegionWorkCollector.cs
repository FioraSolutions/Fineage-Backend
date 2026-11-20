namespace Fineage.Zone.Simulation;

/// <summary>One region's share of a stage tick: its entities in ascending object id order.</summary>
public sealed record RegionWork(RegionId Region, IReadOnlyList<int> ObjectIds);

/// <summary>Buckets the entities a stage collected by the region that owns them.</summary>
public sealed class RegionWorkCollector
{
    readonly Dictionary<RegionId, List<int>> _byRegion = [];

    public void Add(RegionId region, int objectId)
    {
        if (!_byRegion.TryGetValue(region, out var ids))
        {
            ids = [];
            _byRegion[region] = ids;
        }

        ids.Add(objectId);
    }

    /// <summary>Collected work in ascending region order, each region's ids ascending; the collector is empty after.</summary>
    internal List<RegionWork> Drain()
    {
        var work = new List<RegionWork>(_byRegion.Count);
        foreach (var (region, ids) in _byRegion)
        {
            ids.Sort();
            work.Add(new RegionWork(region, ids));
        }

        _byRegion.Clear();
        work.Sort(static (a, b) => a.Region.CompareTo(b.Region));
        return work;
    }
}
