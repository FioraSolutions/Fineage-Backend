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

/// <summary>
/// One region's share of the world: the entities it owns, their active-set memberships and the spatial cell each one
/// stands in (the region-local AOI index).
/// </summary>
public sealed class RegionContainer
{
    internal const ActiveSets All = ActiveSets.Moving | ActiveSets.Thinking | ActiveSets.AiBusy | ActiveSets.PendingHit |
                                    ActiveSets.Decaying | ActiveSets.NeedRegen | ActiveSets.WithAbnormals;
    internal static readonly int SetCount = BitOperations.PopCount((uint)All);

    readonly Dictionary<int, long> _members = [];
    readonly Dictionary<long, HashSet<int>> _cells = [];
    readonly HashSet<int>[] _active = Enumerable.Range(0, SetCount).Select(_ => new HashSet<int>()).ToArray();

    internal RegionContainer(RegionId id) => Id = id;

    public RegionId Id { get; }

    public int EntityCount => _members.Count;

    public bool Owns(int objectId) => _members.ContainsKey(objectId);

    public int CountOf(ActiveSets set) => _active[IndexOf(set)].Count;

    public bool IsIn(int objectId, ActiveSets set) => _active[IndexOf(set)].Contains(objectId);

    /// <summary>Members of <paramref name="set"/> in ascending object id order.</summary>
    public List<int> Snapshot(ActiveSets set)
    {
        var ids = _active[IndexOf(set)].ToList();
        ids.Sort();
        return ids;
    }

    internal IEnumerable<int> Members => _members.Keys;

    internal long CellOf(int objectId) => _members[objectId];

    internal void Install(int objectId, ActiveSets memberships, long cell)
    {
        if (!_members.TryAdd(objectId, cell))
            throw new InvalidOperationException($"region {Id} already owns entity {objectId}");
        CellMembers(cell).Add(objectId);
        foreach (var set in Each(memberships))
            _active[IndexOf(set)].Add(objectId);
    }

    internal void Uninstall(int objectId, ActiveSets memberships)
    {
        if (!_members.Remove(objectId, out long cell))
            throw new InvalidOperationException($"region {Id} does not own entity {objectId}");
        DropFromCell(cell, objectId);
        foreach (var set in Each(memberships))
            _active[IndexOf(set)].Remove(objectId);
    }

    internal void MoveToCell(int objectId, long cell)
    {
        long previous = _members[objectId];
        if (previous == cell)
            return;
        DropFromCell(previous, objectId);
        _members[objectId] = cell;
        CellMembers(cell).Add(objectId);
    }

    /// <summary>Adds the owned entities standing in the cells <c>[x0..x1] × [y0..y1]</c> to <paramref name="into"/>.</summary>
    internal void CollectCells(int x0, int y0, int x1, int y1, List<int> into)
    {
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (_cells.TryGetValue(SpatialCells.Pack(x, y), out var ids))
                    into.AddRange(ids);
            }
        }
    }

    HashSet<int> CellMembers(long cell)
    {
        if (!_cells.TryGetValue(cell, out var ids))
        {
            ids = [];
            _cells.Add(cell, ids);
        }

        return ids;
    }

    void DropFromCell(long cell, int objectId)
    {
        var ids = _cells[cell];
        ids.Remove(objectId);
        if (ids.Count == 0)
            _cells.Remove(cell);
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
/// systems that scan the whole world see the order they saw before regions owned entities. Each container indexes
/// its entities by <see cref="CellSize"/> cell, which is where spatial queries look. Inside a region body
/// (<see cref="EnterBody"/>) the directory refuses what would touch another region: ownership changes, relocation,
/// and set changes of entities the body's region does not own.
/// </summary>
public sealed class RegionDirectory<T> where T : class, IRegionMember
{
    public const int DefaultCellSize = 3000;

    [ThreadStatic] static RegionDirectory<T>? t_bodyOf;
    [ThreadStatic] static RegionId t_bodyRegion;

    readonly IRegionPartition _partition;
    readonly Dictionary<int, T> _entities = [];
    readonly Dictionary<int, Owner> _owners = [];
    readonly SortedDictionary<RegionId, RegionContainer> _regions = [];
    readonly HashSet<int> _relocated = [];
    readonly HashSet<RegionId> _borderDirty = [];
    readonly Dictionary<RegionId, List<T>> _borderCache = [];
    readonly Action<string>? _requireOwner;

    sealed class Owner(RegionContainer region)
    {
        public RegionContainer Region { get; set; } = region;
        public long Epoch { get; set; }
    }

    /// <param name="requireOwner">
    /// Called with what is about to happen on every access outside a region body; throws when the calling thread does
    /// not own the directory (the world's simulation thread).
    /// </param>
    public RegionDirectory(IRegionPartition partition, int cellSize = DefaultCellSize, Action<string>? requireOwner = null)
    {
        _partition = partition ?? throw new ArgumentNullException(nameof(partition));
        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Spatial cell size must be positive.");
        CellSize = cellSize;
        _requireOwner = requireOwner;
    }

    void Touch(string what)
    {
        if (_requireOwner != null && !ReferenceEquals(t_bodyOf, this))
            _requireOwner(what);
    }

    public IRegionPartition Partition => _partition;

    /// <summary>Side of the spatial cells each region indexes its entities by.</summary>
    public int CellSize { get; }

    public int Count
    {
        get
        {
            Touch("counting entities");
            return _entities.Count;
        }
    }

    public Dictionary<int, T>.ValueCollection Values
    {
        get
        {
            Touch("listing entities");
            return _entities.Values;
        }
    }

    public Dictionary<int, T>.KeyCollection Keys
    {
        get
        {
            Touch("listing entity ids");
            return _entities.Keys;
        }
    }

    /// <summary>Regions that own at least one entity, ascending.</summary>
    public IReadOnlyCollection<RegionContainer> Regions
    {
        get
        {
            Touch("listing regions");
            return _regions.Values;
        }
    }

    /// <summary>The region bucket for <paramref name="region"/>, when it owns at least one entity.</summary>
    public bool TryGetContainer(RegionId region, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out RegionContainer container)
    {
        Touch("reading regions");
        return _regions.TryGetValue(region, out container);
    }

    /// <summary>The region whose body runs on this thread, if any.</summary>
    public RegionId? CurrentBody => ReferenceEquals(t_bodyOf, this) ? t_bodyRegion : null;

    public bool ContainsKey(int objectId)
    {
        Touch("reading entities");
        return _entities.ContainsKey(objectId);
    }

    public bool TryGetValue(int objectId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T entity)
    {
        Touch("reading entities");
        return _entities.TryGetValue(objectId, out entity);
    }

    public T? GetValueOrDefault(int objectId)
    {
        Touch("reading entities");
        return _entities.GetValueOrDefault(objectId);
    }

    /// <summary>Reading throws for an unknown id; writing places the entity, replacing the one with that id.</summary>
    public T this[int objectId]
    {
        get
        {
            Touch("reading entities");
            return _entities.TryGetValue(objectId, out var entity)
                ? entity
                : throw new KeyNotFoundException($"entity {objectId} is not in the world");
        }
        set
        {
            Remove(objectId);
            Add(objectId, value);
        }
    }

    /// <summary>
    /// Marks the calling thread as running <paramref name="region"/>'s body until disposed. Bodies do not nest: a
    /// thread runs one region at a time.
    /// </summary>
    public BodyScope EnterBody(RegionId region)
    {
        if (t_bodyOf != null)
            throw new InvalidOperationException($"region {region}'s body started while region {t_bodyRegion}'s body runs on this thread");
        t_bodyOf = this;
        t_bodyRegion = region;
        return new BodyScope(this);
    }

    public readonly struct BodyScope : IDisposable
    {
        readonly RegionDirectory<T>? _directory;

        internal BodyScope(RegionDirectory<T> directory) => _directory = directory;

        public void Dispose()
        {
            if (_directory != null && ReferenceEquals(t_bodyOf, _directory))
                t_bodyOf = null;
        }
    }

    /// <summary>Places <paramref name="entity"/> in the region of its position, with the sets it already joined.</summary>
    public void Add(int objectId, T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        RequireOutsideBody($"placing entity {objectId}");
        Touch($"placing entity {objectId}");
        if (entity.ObjectId != objectId)
            throw new ArgumentException($"entity {entity.ObjectId} cannot be placed under id {objectId}", nameof(objectId));
        if (_entities.ContainsKey(objectId))
            throw new ArgumentException($"entity {objectId} is already in the world", nameof(objectId));

        var region = RegionAt(_partition.RegionOf(entity.InstanceId, entity.X, entity.Y));
        region.Install(objectId, entity.ActiveSets, CellKey(entity));
        _entities.Add(objectId, entity);
        _owners.Add(objectId, new Owner(region));
        _borderDirty.Add(region.Id);
    }

    /// <summary>Takes the entity out of its region and every active set.</summary>
    public bool Remove(int objectId)
    {
        RequireOutsideBody($"removing entity {objectId}");
        Touch($"removing entity {objectId}");
        if (!_entities.Remove(objectId, out var entity))
            return false;
        _owners.Remove(objectId, out var owner);
        _borderDirty.Add(owner!.Region.Id);
        owner.Region.Uninstall(objectId, entity.ActiveSets);
        DropIfEmpty(owner.Region);
        entity.ActiveSets = ActiveSets.None;
        _relocated.Remove(objectId);
        return true;
    }

    /// <summary>The owner of <paramref name="entity"/>; throws when this object is not placed.</summary>
    public RegionPlacement OwnerOf(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Touch("reading region owners");
        if (!_entities.TryGetValue(entity.ObjectId, out var placed) || !ReferenceEquals(placed, entity))
            throw new InvalidOperationException($"entity {entity.ObjectId} is not placed in any region");
        var owner = _owners[entity.ObjectId];
        return new RegionPlacement(owner.Region.Id, owner.Epoch);
    }

    public bool TryOwnerOf(int objectId, out RegionPlacement placement)
    {
        Touch("reading region owners");
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
        RegionContainer.IndexOf(set);
        Touch("changing active sets");
        if ((entity.ActiveSets & set) != 0)
            return;
        var owner = OwnerForSetChange(entity, set);
        entity.ActiveSets |= set;
        owner?.Region.Join(entity.ObjectId, set);
    }

    public void Leave(T entity, ActiveSets set)
    {
        RegionContainer.IndexOf(set);
        Touch("changing active sets");
        if ((entity.ActiveSets & set) == 0)
            return;
        var owner = OwnerForSetChange(entity, set);
        entity.ActiveSets &= ~set;
        owner?.Region.Leave(entity.ObjectId, set);
    }

    /// <summary>Placed entities in <paramref name="set"/>.</summary>
    public int CountOf(ActiveSets set)
    {
        RegionContainer.IndexOf(set);
        RequireOutsideBody($"counting {set} across regions");
        int count = 0;
        foreach (var region in _regions.Values)
            count += region.CountOf(set);
        return count;
    }

    /// <summary>Members of <paramref name="set"/> region by region in ascending order, each region's ids ascending.</summary>
    public int[] Snapshot(ActiveSets set)
    {
        RequireOutsideBody($"listing {set} across regions");
        var ids = new List<int>();
        foreach (var region in _regions.Values)
            ids.AddRange(region.Snapshot(set));
        return [.. ids];
    }

    /// <summary>Adds the members of <paramref name="set"/> to <paramref name="work"/> under their owning region.</summary>
    public void Collect(ActiveSets set, RegionWorkCollector work)
    {
        RequireOutsideBody($"collecting {set}");
        foreach (var region in _regions.Values)
        {
            foreach (int id in region.Snapshot(set))
                work.Add(region.Id, id);
        }
    }

    /// <summary>
    /// A position or instance change: the owner's cell index follows at once, and the next <see cref="Handoff"/>
    /// checks the owner.
    /// </summary>
    public void Relocated(int objectId)
    {
        if (!_entities.TryGetValue(objectId, out var entity))
            return;
        RequireOutsideBody($"relocating entity {objectId}");
        var owner = _owners[objectId];
        owner.Region.MoveToCell(objectId, CellKey(entity));
        _relocated.Add(objectId);
        _borderDirty.Add(owner.Region.Id);
    }

    /// <summary>
    /// The end-of-tick barrier: every entity relocated since the last call, in ascending id order, moves to the region
    /// of its position when its owner no longer holds it (<see cref="IRegionPartition.Holds"/>, which applies the
    /// partition's hysteresis). Memberships move with the entity and the epoch rises.
    /// </summary>
    public IReadOnlyList<RegionTransfer> Handoff()
    {
        RequireOutsideBody("a handoff");
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
            long cell = from.CellOf(id);
            from.Uninstall(id, entity.ActiveSets);
            to.Install(id, entity.ActiveSets, cell);
            DropIfEmpty(from);
            owner.Region = to;
            owner.Epoch++;
            _borderDirty.Add(from.Id);
            _borderDirty.Add(to.Id);
            transfers.Add(new RegionTransfer(id, from.Id, to.Id, owner.Epoch));
        }

        return transfers;
    }

    /// <summary>The spatial cell <paramref name="objectId"/> is indexed in, with its owner's instance.</summary>
    public bool TryCellOf(int objectId, out int instanceId, out int cellX, out int cellY)
    {
        if (_owners.TryGetValue(objectId, out var owner))
        {
            instanceId = owner.Region.Id.InstanceId;
            (cellX, cellY) = SpatialCells.Unpack(owner.Region.CellOf(objectId));
            return true;
        }

        instanceId = cellX = cellY = 0;
        return false;
    }

    /// <summary>
    /// Adds every entity of <paramref name="instanceId"/> standing in the cells <c>[x0..x1] × [y0..y1]</c> to
    /// <paramref name="into"/>, whichever region owns it. Reads every overlapping region: the scheduler thread only.
    /// </summary>
    public void InCells(int instanceId, int x0, int y0, int x1, int y1, List<int> into)
    {
        RequireOutsideBody("a cross-region cell query");
        foreach (var id in _partition.RegionsOverlapping(instanceId, x0 * CellSize, y0 * CellSize,
                     (x1 + 1) * CellSize - 1, (y1 + 1) * CellSize - 1))
        {
            if (_regions.TryGetValue(id, out var region))
                region.CollectCells(x0, y0, x1, y1, into);
        }
    }

    /// <summary>
    /// Entities <paramref name="region"/> owns within <paramref name="radius"/> (2D) of the point, ascending ids: the
    /// live half of a region body's spatial query (the other half reads the neighbours' border snapshots).
    /// </summary>
    public void OwnedNear(RegionId region, int x, int y, int radius, List<T> into)
    {
        if (!_regions.TryGetValue(region, out var container))
            return;
        var ids = new List<int>();
        container.CollectCells(SpatialCells.Of(x - radius, CellSize), SpatialCells.Of(y - radius, CellSize),
            SpatialCells.Of(x + radius, CellSize), SpatialCells.Of(y + radius, CellSize), ids);
        ids.Sort();
        long range2 = (long)radius * radius;
        foreach (int id in ids)
        {
            var entity = _entities[id];
            long dx = entity.X - (long)x, dy = entity.Y - (long)y;
            if (dx * dx + dy * dy <= range2)
                into.Add(entity);
        }
    }

    /// <summary>
    /// The entities of each region, ascending, that a neighbour's query of <paramref name="reach"/> could see: those not
    /// deep inside the region's own cell (<see cref="IRegionPartition.IsInterior"/>), including any still owned past
    /// the edge. What a border snapshot publishes.
    /// </summary>
    public IEnumerable<(RegionId Region, List<T> Members)> BorderMembers(int reach)
    {
        RequireOutsideBody("publishing border snapshots");
        foreach (var region in _regions.Values)
        {
            if (_borderCache.TryGetValue(region.Id, out var cached) && !_borderDirty.Contains(region.Id))
            {
                yield return (region.Id, cached);
                continue;
            }

            var members = new List<T>();
            foreach (int id in region.Members)
            {
                var entity = _entities[id];
                if (!_partition.IsInterior(region.Id, entity.InstanceId, entity.X, entity.Y, reach))
                    members.Add(entity);
            }

            members.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
            _borderCache[region.Id] = members;
            yield return (region.Id, members);
        }

        _borderDirty.Clear();
    }

    /// <summary>
    /// Checks that every entity has exactly one owner, that owners hold their entity's position, that each entity is
    /// indexed in the cell it stands in, that region active sets match the entities' memberships and that the counts
    /// add up. Throws with every violation found.
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
            {
                problems.Add($"entity {id} is routed to region {region.Id}, which does not hold it");
                continue;
            }
            if (!_relocated.Contains(id) && !_partition.Holds(region.Id, entity.InstanceId, entity.X, entity.Y))
                problems.Add($"entity {id} at {entity.InstanceId}:{entity.X},{entity.Y} is outside its region {region.Id} and was not relocated");
            if (region.CellOf(id) != CellKey(entity))
                problems.Add($"entity {id} at {entity.X},{entity.Y} is indexed in cell {SpatialCells.Unpack(region.CellOf(id))} of region {region.Id}");
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

    long CellKey(T entity) => SpatialCells.KeyAt(entity.X, entity.Y, CellSize);

    /// <summary>The owner whose sets change with the entity's flags; null while the entity is not placed.</summary>
    Owner? OwnerForSetChange(T entity, ActiveSets set)
    {
        var owner = _entities.TryGetValue(entity.ObjectId, out var placed) && ReferenceEquals(placed, entity)
            ? _owners[entity.ObjectId]
            : null;
        if (CurrentBody is { } body && owner?.Region.Id != body)
        {
            string where = owner == null ? "is not placed" : $"is owned by region {owner.Region.Id}";
            throw new InvalidOperationException(
                $"region {body}'s body changed {set} of entity {entity.ObjectId}, which {where}: a body changes only its own region's entities");
        }

        return owner;
    }

    void RequireOutsideBody(string action)
    {
        if (CurrentBody is { } body)
            throw new InvalidOperationException(
                $"{action} inside region {body}'s body: ownership and positions change only on the scheduler thread");
    }

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
}
