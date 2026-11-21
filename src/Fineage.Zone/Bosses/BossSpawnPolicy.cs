namespace Fineage.Zone.Bosses;

/// <summary>
/// Whether a catalog boss row should spawn on this Zone process (shared with spawns'
/// <see cref="ZoneMapFilter"/> bounds). Datapack <see cref="BossDefinition.Enabled"/> false
/// means "not in the narrow dev slice"; whole-world hosts still spawn rows inside the active filter.
/// </summary>
public interface IBossSpawnPolicy
{
    bool ShouldSpawn(BossDefinition definition);
}

/// <summary>Only bosses with <c>enabled: true</c> in <c>raidbosses.json</c>.</summary>
public sealed class DatapackBossSpawnPolicy : IBossSpawnPolicy
{
    public static readonly DatapackBossSpawnPolicy Instance = new();

    public bool ShouldSpawn(BossDefinition definition) => definition.Enabled;
}

/// <summary>
/// Hosted default: every boss with a real spawn point in the active map filter (or all when unbounded).
/// Script-only rows (no respawn window at 0,0) stay off unless explicitly enabled in JSON.
/// </summary>
public sealed class MapFilteredBossSpawnPolicy(bool includeEnabledMaps) : IBossSpawnPolicy
{
    public bool ShouldSpawn(BossDefinition definition)
    {
        if (definition.Enabled)
            return true;
        if (!includeEnabledMaps)
            return false;
        if (!definition.HasRespawnWindow && definition is { X: 0, Y: 0, Z: 0 })
            return false;
        return ZoneMapFilter.Contains(ZoneMapFilter.BoundsList, definition.X, definition.Y);
    }
}