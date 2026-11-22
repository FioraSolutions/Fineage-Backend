namespace Fineage.Zone.Bosses;

/// <summary>
/// Whether a catalog boss row should spawn on this Zone process (shared with spawns'
/// <see cref="ZoneMapFilter"/> bounds). <see cref="BossDefinition.Enabled"/> false keeps script-only
/// bosses off until their system spawns them; whole-world hosts materialize every other retail row.
/// </summary>
public interface IBossSpawnPolicy
{
    bool ShouldSpawn(BossDefinition definition);
}

/// <summary>
/// Smoke hosts: <c>enabled: true</c> rows that have a Mobius world spawn point.
/// </summary>
public sealed class DatapackBossSpawnPolicy : IBossSpawnPolicy
{
    public static readonly DatapackBossSpawnPolicy Instance = new();

    public bool ShouldSpawn(BossDefinition definition) =>
        definition.Enabled && definition.HasWorldSpawnPoint;
}

/// <summary>
/// Same gate as spawn maps: an enabled row with a world point, inside
/// <see cref="ZoneMapFilter.BoundsList"/> when that filter is set. Empty bounds keep every such row.
/// Single-map hosts still require an explicit box so a smoke world does not materialize the catalog.
/// </summary>
public sealed class MapFilteredBossSpawnPolicy(bool includeEnabledMaps) : IBossSpawnPolicy
{
    public bool ShouldSpawn(BossDefinition definition)
    {
        if (!definition.Enabled || !definition.HasWorldSpawnPoint)
            return false;
        var bounds = ZoneMapFilter.BoundsList;
        if (!includeEnabledMaps && bounds.Count == 0)
            return false;
        return ZoneMapFilter.Contains(bounds, definition.X, definition.Y);
    }
}