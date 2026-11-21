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
/// Smoke hosts: <c>enabled: true</c> rows that are also on the Mobius world spawn list
/// (coordinates + respawn window).
/// </summary>
public sealed class DatapackBossSpawnPolicy : IBossSpawnPolicy
{
    public static readonly DatapackBossSpawnPolicy Instance = new();

    public bool ShouldSpawn(BossDefinition definition) =>
        definition.Enabled && definition.IsWorldScheduledSpawn;
}

/// <summary>
/// Hosted default: retail bosses from <c>raidbosses.json</c> when whole-world maps are on. Single-map /
/// smoke worlds spawn only <c>enabled</c> rows that also lie inside <see cref="ZoneMapFilter.BoundsList"/> when set.
/// </summary>
public sealed class MapFilteredBossSpawnPolicy(bool includeEnabledMaps) : IBossSpawnPolicy
{
    public bool ShouldSpawn(BossDefinition definition)
    {
        if (!definition.IsWorldScheduledSpawn)
            return false;

        if (!includeEnabledMaps)
        {
            if (!definition.Enabled)
                return false;
            var bounds = ZoneMapFilter.BoundsList;
            return bounds.Count > 0 && ZoneMapFilter.Contains(bounds, definition.X, definition.Y);
        }

        if (definition.Enabled)
            return true;
        var regional = ZoneMapFilter.BoundsList;
        return regional.Count > 0 && ZoneMapFilter.Contains(regional, definition.X, definition.Y);
    }
}