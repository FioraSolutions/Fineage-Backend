namespace Fineage.Zone.Simulation;

/// <summary>
/// A system that runs once per region on every tick it is due. <see cref="Collect"/> gathers this tick's entities
/// into regions on the scheduler thread; <see cref="RunRegion"/> then processes one region's entities in ascending
/// object id order. Under a parallel executor <see cref="RunRegion"/> runs concurrently for different regions, so it
/// may only touch state owned by that region.
/// </summary>
public interface IRegionStage
{
    string Name { get; }

    void Collect(TickContext tick, RegionWorkCollector work);

    void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds);
}

/// <summary><see cref="IRegionStage"/> built from delegates, for systems that live as world partials.</summary>
public sealed class RegionStage(
    string name,
    Action<TickContext, RegionWorkCollector> collect,
    Action<TickContext, int> runEntity) : IRegionStage
{
    public string Name { get; } = name;

    public void Collect(TickContext tick, RegionWorkCollector work) => collect(tick, work);

    public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds)
    {
        foreach (int objectId in objectIds)
            runEntity(tick, objectId);
    }
}
