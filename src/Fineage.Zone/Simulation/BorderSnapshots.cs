namespace Fineage.Zone.Simulation;

/// <summary>What a neighbouring region may know about an entity near the edge: an immutable record, nothing live.</summary>
public interface IRegionPresence
{
    int ObjectId { get; }

    int InstanceId { get; }

    int X { get; }

    int Y { get; }
}

/// <summary>
/// The border snapshots of section 3.5. At the end of every step each region publishes immutable presence records of
/// the entities a neighbour's query could reach (<see cref="RegionDirectory{T}.BorderMembers"/>); region bodies of the
/// next step see other regions only through them. A border therefore costs one step of latency, the same under every
/// executor, and a body never reads state another thread may be writing.
/// </summary>
public sealed class BorderSnapshots<TPresence> where TPresence : IRegionPresence
{
    readonly IRegionPartition _partition;
    IReadOnlyDictionary<RegionId, IReadOnlyList<TPresence>> _published = new Dictionary<RegionId, IReadOnlyList<TPresence>>();

    /// <param name="reach">How far from its edge a region publishes; a query reaches at most this minus the handoff margin.</param>
    public BorderSnapshots(IRegionPartition partition, int reach)
    {
        _partition = partition ?? throw new ArgumentNullException(nameof(partition));
        if (reach <= partition.HandoffMargin)
            throw new ArgumentOutOfRangeException(nameof(reach), reach,
                $"Border reach must exceed the {partition.HandoffMargin}-unit handoff margin.");
        Reach = reach;
    }

    public int Reach { get; }

    /// <summary>The longest radius <see cref="NeighboursNear"/> answers completely.</summary>
    public int MaxQueryRadius => Reach - _partition.HandoffMargin;

    /// <summary>The step the current snapshots were taken at; -1 before the first.</summary>
    public long Tick { get; private set; } = -1;

    /// <summary>On the scheduler thread, between stages: replaces every region's snapshot at once.</summary>
    public void Publish(long tick, IEnumerable<(RegionId Region, IReadOnlyList<TPresence> Members)> snapshots)
    {
        var published = new Dictionary<RegionId, IReadOnlyList<TPresence>>();
        foreach (var (region, members) in snapshots)
        {
            if (members.Count > 0)
                published.Add(region, members);
        }

        _published = published;
        Tick = tick;
    }

    /// <summary>The snapshot <paramref name="region"/> published, empty when it had nothing near an edge.</summary>
    public IReadOnlyList<TPresence> Of(RegionId region) => _published.GetValueOrDefault(region) ?? [];

    /// <summary>
    /// Presences within <paramref name="radius"/> (2D) of the point published by regions other than
    /// <paramref name="reader"/>, in ascending object id order. With the reader's own live entities this is everything
    /// in range, as long as the point is held by the reader and the radius is at most <see cref="MaxQueryRadius"/>.
    /// </summary>
    public void NeighboursNear(RegionId reader, int x, int y, int radius, List<TPresence> into)
    {
        if (radius < 0 || radius > MaxQueryRadius)
            throw new ArgumentOutOfRangeException(nameof(radius), radius,
                $"Border snapshots reach {Reach} units, so a query from region {reader} reaches at most {MaxQueryRadius}.");
        int first = into.Count;
        long range2 = (long)radius * radius;
        foreach (var region in _partition.RegionsOverlapping(reader.InstanceId, x - radius, y - radius, x + radius, y + radius))
        {
            if (region == reader || !_published.TryGetValue(region, out var members))
                continue;
            foreach (var presence in members)
            {
                long dx = presence.X - (long)x, dy = presence.Y - (long)y;
                if (presence.InstanceId == reader.InstanceId && dx * dx + dy * dy <= range2)
                    into.Add(presence);
            }
        }

        into.Sort(first, into.Count - first, Comparer<TPresence>.Create(static (a, b) => a.ObjectId.CompareTo(b.ObjectId)));
    }
}
