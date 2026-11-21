using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Region ownership (step 7 of <c>docs/architecture/zone-simulation.md</c>): <see cref="_byId"/> places every entity in
/// the region of its position, active sets live in the owning region, and the last stage of every step hands moved
/// entities to their new owner.
/// </summary>
public sealed partial class ZoneWorld
{
    internal const string HandoffStage = "handoff";

    void HandOff(TickContext tick)
    {
        var transfers = _byId.Handoff();
        if (transfers.Count > 0)
            _simulationObserver.EntitiesHandedOff(tick, transfers);
        PublishBorders(tick);
    }

    /// <summary>Test and diagnostics: the region that owns <paramref name="objectId"/> and the epoch of that route.</summary>
    internal RegionPlacement RegionOwnerOf(int objectId)
    {
        using (_affinity.Step())
        {
            return _byId.TryOwnerOf(objectId, out var placement)
                ? placement
                : throw new KeyNotFoundException($"entity {objectId} is not in the world");
        }
    }

    /// <summary>Test: places a bare entity without a session, AOI entry or fanout.</summary>
    internal void PlaceForTests(ZoneEntity entity)
    {
        using (_affinity.Step())
            _byId.Add(entity.ObjectId, entity);
    }

    /// <summary>Test and diagnostics: throws when an entity has no owner or two, or a region's sets drifted.</summary>
    internal void VerifyRegions()
    {
        using (_affinity.Step())
            _byId.Verify();
    }
}
