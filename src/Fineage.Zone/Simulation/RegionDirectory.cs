using System.Numerics;

namespace Fineage.Zone.Simulation;

/// <summary>
/// The scheduling sets an owned entity can be in. An entity carries its memberships, so it keeps them across a
/// handoff and can join a set before it is placed; the owning region indexes them for its stages.
/// </summary>
[Flags]
public enum ActiveSets
{
    None = 0,
    Moving = 1 << 0,
    Thinking = 1 << 1,
    AiBusy = 1 << 2,
    PendingHit = 1 << 3,
    Decaying = 1 << 4,
    NeedRegen = 1 << 5,
    WithAbnormals = 1 << 6,
}

/// <summary>What a region needs to know about an entity it owns.</summary>
public interface IRegionMember
{
    int ObjectId { get; }

    int InstanceId { get; }

    int X { get; }

    int Y { get; }

    /// <summary>Written only by <see cref="RegionDirectory{T}"/>.</summary>
    ActiveSets ActiveSets { get; set; }
}

/// <summary>Where an entity is owned. The epoch rises on every handoff, so a stale route can be told apart.</summary>
public readonly record struct RegionPlacement(RegionId Region, long Epoch);

/// <summary>An owner change made at the end-of-tick barrier: the in-process <c>RouteUpdate</c>.</summary>
public readonly record struct RegionTransfer(int ObjectId, RegionId From, RegionId To, long Epoch);

/// <summary>One region's share of the world: the entities it owns and their active-set memberships.</summary>
public sealed class RegionContainer
{
    internal const ActiveSets All = ActiveSets.Moving | ActiveSets.Thinking | ActiveSets.AiBusy | ActiveSets.PendingHit |
                                    ActiveSets.Decaying | ActiveSets.NeedRegen | ActiveSets.WithAbnormals;
    internal static readonly int SetCount = BitOperations.PopCount((uint)All);

    readonly HashSet<int> _members = [];
    readonly HashSet<int>[] _active = Enumerable.Range(0, SetCount).Select(_ => new HashSet<int>()).ToArray();

    internal RegionContainer(RegionId id) => Id = id;

    public RegionId Id { get; }

    public int EntityCount => _members.Count;

    public bool Owns(int objectId) => _members.Contains(objectId);

    public int CountOf(ActiveSets set) => _active[IndexOf(set)].Count;

    public bool IsIn(int objectId, ActiveSets set) => _active[IndexOf(set)].Contains(objectId);

    /// <summary>Members of <paramref name="set"/> in ascending object id order.</summary>
    public List<int> Snapshot(ActiveSets set)
    {
        var ids = _active[IndexOf(set)].ToList();
        ids.Sort();
        return ids;
    }

    internal IEnumerable<int> Members => _members;

    internal void Install(int objectId, ActiveSets memberships)
    {
        if (!_members.Add(objectId))
            throw new InvalidOperationException($"region {Id} already owns entity {objectId}");
        foreach (var set in Each(memberships))
            _active[IndexOf(set)].Add(objectId);
    }

    internal void Uninstall(int objectId, ActiveSets memberships)
    {
        if (!_members.Remove(objectId))
            throw new InvalidOperationException($"region {Id} does not own entity {objectId}");
        foreach (var set in Each(memberships))
            _active[IndexOf(set)].Remove(objectId);
    }

    internal void Join(int objectId, ActiveSets set) => _active[IndexOf(set)].Add(objectId);

    internal void Leave(int objectId, ActiveSets set) => _active[IndexOf(set)].Remove(objectId);

    internal static IEnumerable<ActiveSets> Each(ActiveSets memberships)
    {
        for (int i = 0; i < SetCount; i++)
        {
            var set = (ActiveSets)(1 << i);
            if ((memberships & set) != 0)
                yield return set;
        }
    }

    internal static int IndexOf(ActiveSets set)
    {
        if (set == ActiveSets.None || (set & ~All) != 0 || !BitOperations.IsPow2((uint)set))
            throw new ArgumentOutOfRangeException(nameof(set), set, "Expected exactly one active set.");
        return BitOperations.TrailingZeroCount((uint)set);
    }
}

/// <summary>
/// The in-process <c>IRegionDirectory</c> of section 3.7: every entity of the world is owned by exactly one
/// <see cref="RegionContainer"/>, chosen by the partition when the entity is placed and changed only by
/// <see cref="Handoff"/> at the end-of-tick barrier. Lookups by id keep dictionary semantics and insertion order, so
/// systems that scan the whole world see the order they saw before regions owned entities.
/// </summary>
public sealed class RegionDirectory<T> where T : class, IRegionMember
{
    readonly IRegionPartition _partition;
    readonly Dictionary<int, T> _entities = [];
    readonly Dictionary<int, Owner> _owners = [];
    readonly SortedDictionary<RegionId, RegionContainer> _regions = [];
    readonly int[] _activeCounts = new int[RegionContainer.SetCount];
    readonly HashSet<int> _relocated = [];

    sealed class Owner(RegionContainer region)
    {
        public RegionContainer Region { get; set; } = region;
        public long Epoch { get; set; }
    }

    public RegionDirectory(IRegionPartition partition)
    {
        _partition = partition ?? throw new ArgumentNullException(nameof(partition));
    }

    public int Count => _entities.Count;

    public Dictionary<int, T>.ValueCollection Values => _entities.Values;

    public Dictionary<int, T>.KeyCollection Keys => _entities.Keys;

    /// <summary>Regions that own at least one entity, ascending.</summary>
    public IReadOnlyCollection<RegionContainer> Regions => _regions.Values;

    public bool ContainsKey(int objectId) => _entities.ContainsKey(objectId);

    public bool TryGetValue(int objectId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T entity) =>
        _entities.TryGetValue(objectId, out entity);

    public T? GetValueOrDefault(int objectId) => _entities.GetValueOrDefault(objectId);

    /// <summary>Reading throws for an unknown id; writing places the entity, replacing the one with that id.</summary>
    public T this[int objectId]
    {
        get => _entities.TryGetValue(objectId, out var entity)
            ? entity
            : throw new KeyNotFoundException($"entity {objectId} is not in the world");
        set
        {
            Remove(objectId);
            Add(objectId, value);
        }
    }

    /// <summary>Places <paramref name="entity"/> in the region of its position, with the sets it already joined.</summary>
    public void Add(int objectId, T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.ObjectId != objectId)
            throw new ArgumentException($"entity {entity.ObjectId} cannot be placed under id {objectId}", nameof(objectId));
        if (_entities.ContainsKey(objectId))
            throw new ArgumentException($"entity {objectId} is already in the world", nameof(objectId));

        var region = RegionAt(_partition.RegionOf(entity.InstanceId, entity.X, entity.Y));
        region.Install(objectId, entity.ActiveSets);
        _entities.Add(objectId, entity);
        _owners.Add(objectId, new Owner(region));
        CountMemberships(entity.ActiveSets, +1);
    }

    /// <summary>Takes the entity out of its region and every active set.</summary>
    public bool Remove(int objectId)
    {
        if (!_entities.Remove(objectId, out var entity))
            return false;
        _owners.Remove(objectId, out var owner);
        owner!.Region.Uninstall(objectId, entity.ActiveSets);
        DropIfEmpty(owner.Region);
        CountMemberships(entity.ActiveSets, -1);
        entity.ActiveSets = ActiveSets.None;
        _relocated.Remove(objectId);
        return true;
    }

    /// <summary>The owner of <paramref name="entity"/>; throws when this object is not placed.</summary>
    public RegionPlacement OwnerOf(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!_entities.TryGetValue(entity.ObjectId, out var placed) || !ReferenceEquals(placed, entity))
            throw new InvalidOperationException($"entity {entity.ObjectId} is not placed in any region");
        var owner = _owners[entity.ObjectId];
        return new RegionPlacement(owner.Region.Id, owner.Epoch);
    }

    public bool TryOwnerOf(int objectId, out RegionPlacement placement)
    {
        if (_owners.TryGetValue(objectId, out var owner))
        {
            placement = new RegionPlacement(owner.Region.Id, owner.Epoch);
            return true;
        }

        placement = default;
        return false;
    }

    public void Join(T entity, ActiveSets set)
    {
        int index = RegionContainer.IndexOf(set);
        if ((entity.ActiveSets & set) != 0)
            return;
        entity.ActiveSets |= set;
        if (PlacedOwner(entity) is { } owner)
        {
            owner.Region.Join(entity.ObjectId, set);
            _activeCounts[index]++;
        }
    }

    public void Leave(T entity, ActiveSets set)
    {
        int index = RegionContainer.IndexOf(set);
        if ((entity.ActiveSets & set) == 0)
            return;
        entity.ActiveSets &= ~set;
        if (PlacedOwner(entity) is { } owner)
        {
            owner.Region.Leave(entity.ObjectId, set);
            _activeCounts[index]--;
        }
    }

    /// <summary>Placed entities in <paramref name="set"/>.</summary>
    public int CountOf(ActiveSets set) => _activeCounts[RegionContainer.IndexOf(set)];

    /// <summary>Members of <paramref name="set"/> region by region in ascending order, each region's ids ascending.</summary>
    public int[] Snapshot(ActiveSets set)
    {
        var ids = new int[CountOf(set)];
        int next = 0;
        foreach (var region in _regions.Values)
        {
            foreach (int id in region.Snapshot(set))
                ids[next++] = id;
        }

        return ids;
    }

    /// <summary>Adds the members of <paramref name="set"/> to <paramref name="work"/> under their owning region.</summary>
    public void Collect(ActiveSets set, RegionWorkCollector work)
    {
        foreach (var region in _regions.Values)
        {
            foreach (int id in region.Snapshot(set))
                work.Add(region.Id, id);
        }
    }

    /// <summary>Marks a position or instance change; the next <see cref="Handoff"/> checks the owner.</summary>
    public void Relocated(int objectId)
    {
        if (_entities.ContainsKey(objectId))
            _relocated.Add(objectId);
    }

    /// <summary>
    /// The end-of-tick barrier: every entity relocated since the last call, in ascending id order, moves to the region
    /// of its position when its owner no longer holds it (<see cref="IRegionPartition.Holds"/>, which applies the
    /// partition's hysteresis). Memberships move with the entity and the epoch rises.
    /// </summary>
    public IReadOnlyList<RegionTransfer> Handoff()
    {
        if (_relocated.Count == 0)
            return [];
        var ids = _relocated.ToList();
        _relocated.Clear();
        ids.Sort();

        var transfers = new List<RegionTransfer>();
        foreach (int id in ids)
        {
            var entity = _entities[id];
            var owner = _owners[id];
            var from = owner.Region;
            if (_partition.Holds(from.Id, entity.InstanceId, entity.X, entity.Y))
                continue;

            var to = RegionAt(_partition.RegionOf(entity.InstanceId, entity.X, entity.Y));
            from.Uninstall(id, entity.ActiveSets);
            to.Install(id, entity.ActiveSets);
            DropIfEmpty(from);
            owner.Region = to;
            owner.Epoch++;
            transfers.Add(new RegionTransfer(id, from.Id, to.Id, owner.Epoch));
        }

        return transfers;
    }

    /// <summary>
    /// Checks that every entity has exactly one owner, that owners hold their entity's position, that region active
    /// sets match the entities' memberships and that the counts add up. Throws with every violation found.
    /// </summary>
    public void Verify()
    {
        var problems = new List<string>();
        var seen = new HashSet<int>();
        foreach (var region in _regions.Values)
        {
            if (region.EntityCount == 0)
                problems.Add($"region {region.Id} is empty but still listed");
            foreach (int id in region.Members)
            {
                if (!seen.Add(id))
                    problems.Add($"entity {id} is owned by more than one region");
                if (!_owners.TryGetValue(id, out var owner) || owner.Region != region)
                    problems.Add($"region {region.Id} holds entity {id} that the directory routes elsewhere");
            }
        }

        foreach (var (id, entity) in _entities)
        {
            var region = _owners[id].Region;
            if (!region.Owns(id))
                problems.Add($"entity {id} is routed to region {region.Id}, which does not hold it");
            if (!_relocated.Contains(id) && !_partition.Holds(region.Id, entity.InstanceId, entity.X, entity.Y))
                problems.Add($"entity {id} at {entity.InstanceId}:{entity.X},{entity.Y} is outside its region {region.Id} and was not relocated");
            foreach (var set in RegionContainer.Each(RegionContainer.All))
            {
                if (region.IsIn(id, set) != ((entity.ActiveSets & set) != 0))
                    problems.Add($"entity {id} membership of {set} differs between the entity and region {region.Id}");
            }
        }

        if (seen.Count != _entities.Count)
            problems.Add($"regions own {seen.Count} entities, the directory lists {_entities.Count}");

        if (problems.Count > 0)
            throw new InvalidOperationException("region ownership is inconsistent: " + string.Join("; ", problems));
    }

    Owner? PlacedOwner(T entity) =>
        _entities.TryGetValue(entity.ObjectId, out var placed) && ReferenceEquals(placed, entity)
            ? _owners[entity.ObjectId]
            : null;

    RegionContainer RegionAt(RegionId id)
    {
        if (!_regions.TryGetValue(id, out var region))
        {
            region = new RegionContainer(id);
            _regions.Add(id, region);
        }

        return region;
    }

    void DropIfEmpty(RegionContainer region)
    {
        if (region.EntityCount == 0)
            _regions.Remove(region.Id);
    }

    void CountMemberships(ActiveSets memberships, int delta)
    {
        foreach (var set in RegionContainer.Each(memberships))
            _activeCounts[RegionContainer.IndexOf(set)] += delta;
    }
}
