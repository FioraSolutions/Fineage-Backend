namespace Fineage.Zone.Simulation;

/// <summary>
/// A system that runs once per region on every tick it is due. <see cref="Collect"/> gathers this tick's entities
/// into regions on the scheduler thread; <see cref="RunRegion"/> then processes one region's entities in ascending
/// object id order, inside the region's <see cref="IRegionScope"/>. A body reads its own region's entities and the
/// neighbours' border snapshots, writes only the entities it processes, and leaves everything else (fan-out,
/// messages to entities) in the scope.
/// </summary>
public interface IRegionStage
{
    string Name { get; }

    /// <summary>
    /// The body keeps to that contract, so the scheduler may run its regions on the parallel executor. Stages that
    /// still read live state of other regions leave this false and always run one region after another.
    /// </summary>
    bool Parallel => false;

    void Collect(TickContext tick, RegionWorkCollector work);

    void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds);
}

/// <summary><see cref="IRegionStage"/> built from delegates, for systems that live as world partials.</summary>
public sealed class RegionStage(
    string name,
    Action<TickContext, RegionWorkCollector> collect,
    Action<TickContext, int> runEntity,
    bool parallel = false) : IRegionStage
{
    public string Name { get; } = name;

    public bool Parallel { get; } = parallel;

    public void Collect(TickContext tick, RegionWorkCollector work) => collect(tick, work);

    public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds)
    {
        foreach (int objectId in objectIds)
            runEntity(tick, objectId);
    }
}
