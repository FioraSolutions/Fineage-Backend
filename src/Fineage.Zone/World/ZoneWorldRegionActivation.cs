using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>WorldRegion</c> activation: NPC think/random-walk runs only in tiles that currently have a player
/// nearby (this region and overlapping neighbours), or while a player watches the mob, or it has drifted far from home.
/// </summary>
public sealed partial class ZoneWorld
{
    readonly HashSet<RegionId> _playerActiveRegions = [];

    /// <summary>Regions with at least one player in range of their tile (test/diagnostics).</summary>
    internal IReadOnlyCollection<RegionId> PlayerActiveRegions => _playerActiveRegions;

    /// <summary>How far past a tile edge we still activate neighbours (one map tile).</summary>
    const int RegionActivationPad = GridRegionPartition.MapTileSize;

    /// <summary>Recomputes Mobius-style active regions from live player positions; call once per AI stage.</summary>
    void RefreshPlayerActiveRegions()
    {
        var next = new HashSet<RegionId>();
        foreach (var player in Players())
        {
            if (player.Session is not { InWorld: true } || player.CurHp <= 0)
                continue;
            foreach (var id in _regions.RegionsOverlapping(
                         player.InstanceId,
                         player.X - RegionActivationPad,
                         player.Y - RegionActivationPad,
                         player.X + RegionActivationPad,
                         player.Y + RegionActivationPad))
                next.Add(id);
        }

        foreach (var id in next)
        {
            if (_playerActiveRegions.Add(id))
                OnRegionActivated(id);
        }

        var retired = new List<RegionId>();
        foreach (var id in _playerActiveRegions)
        {
            if (!next.Contains(id))
                retired.Add(id);
        }

        foreach (var id in retired)
        {
            _playerActiveRegions.Remove(id);
            OnRegionDeactivated(id);
        }
    }

    bool IsPlayerRegionActive(RegionId region) => _playerActiveRegions.Contains(region);

    void OnRegionActivated(RegionId region)
    {
        if (!_byId.TryGetContainer(region, out var container))
            return;
        foreach (int id in container.Members.ToArray())
        {
            if (_byId.TryGetValue(id, out var entity))
                TrackThink(entity);
        }
    }

    void OnRegionDeactivated(RegionId region)
    {
        if (!_byId.TryGetContainer(region, out var container))
            return;
        foreach (int id in container.Snapshot(ActiveSets.Thinking))
        {
            if (_byId.TryGetValue(id, out var npc) && npc.Kind == ZoneEntityKind.Npc)
                ReleaseThink(npc);
        }
    }

    /// <summary>Stagger the first think so active mobs do not pile into one tick.</summary>
    void ScheduleNextThink(ZoneEntity npc, long now)
    {
        int spread = RandomWalkThinkIntervalMs / 2;
        npc.NextThinkMs = now + spread + RndBound(npc, Math.Max(1, spread));
    }

    internal const int RandomWalkThinkIntervalMs = 1000;
}