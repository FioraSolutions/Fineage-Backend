using Fineage.Zone.Drops;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.SeasonalEvents;

// ZoneLog lives in Fineage.Zone.

/// <summary>
/// Shared Mobius <c>LongTimeEvent</c> host for Interlude seasonal scripts (#934):
/// Christmas, L2Day, HeavyMedal. Missing <c>config.xml</c> logs the event id.
/// </summary>
public sealed class SeasonalEventHost
{
    public static readonly string[] RequiredEventIds = ["Christmas", "L2Day", "HeavyMedal"];

    readonly Dictionary<string, SeasonalEventDefinition> _byId;
    readonly Action<string> _warn;

    public SeasonalEventHost(
        IReadOnlyDictionary<string, SeasonalEventDefinition> loaded,
        IEnumerable<string>? missingIds = null,
        Action<string>? warn = null)
    {
        _warn = warn ?? (_ => { });
        _byId = new Dictionary<string, SeasonalEventDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in loaded)
            _byId[pair.Key] = pair.Value;
        foreach (var id in missingIds ?? [])
            _warn($"[seasonal-events] missing event data eventId={id}");
    }

    public int LoadedCount => _byId.Count;

    public bool TryGet(string eventId, out SeasonalEventDefinition def) =>
        _byId.TryGetValue(eventId, out def!);

    public bool IsActive(string eventId, long nowUnixMs) =>
        _byId.TryGetValue(eventId, out var def) && def.IsActiveAt(nowUnixMs);

    public IReadOnlyList<SeasonalEventDefinition> ActiveAt(long nowUnixMs)
    {
        var list = new List<SeasonalEventDefinition>();
        foreach (var def in _byId.Values)
        {
            if (def.IsActiveAt(nowUnixMs))
                list.Add(def);
        }

        return list;
    }

    /// <summary>Drop windows for currently active seasonal events (merged with <see cref="EventDropCatalog"/>).</summary>
    public IReadOnlyList<EventDropWindow> ActiveDropWindows(long nowUnixMs)
    {
        var windows = new List<EventDropWindow>();
        foreach (var def in ActiveAt(nowUnixMs))
        {
            if (def.DropGroups.Count == 0)
                continue;
            windows.Add(new EventDropWindow
            {
                Id = def.Id,
                StartUnixMs = def.StartUnixMs,
                EndUnixMs = def.EndUnixMs,
                MaxKillerLevelGap = 9,
                Groups = def.DropGroups
            });
        }

        return windows;
    }

    public static SeasonalEventHost LoadDefault(Action<string>? warn = null)
    {
        warn ??= msg => ZoneLog.Warn(msg);
        string? dataRoot = GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (dataRoot == null)
        {
            warn("[seasonal-events] data directory not found; no seasonal events loaded");
            return new SeasonalEventHost(
                new Dictionary<string, SeasonalEventDefinition>(),
                RequiredEventIds,
                warn);
        }

        return LoadFromDataRoot(dataRoot, warn);
    }

    public static SeasonalEventHost LoadFromDataRoot(string dataRoot, Action<string>? warn = null)
    {
        warn ??= _ => { };
        var loaded = new Dictionary<string, SeasonalEventDefinition>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var id in RequiredEventIds)
        {
            string path = Path.Combine(dataRoot, "scripts", "events", id, "config.xml");
            var def = SeasonalEventConfigLoader.TryLoad(id, path, warn);
            if (def == null)
                missing.Add(id);
            else
                loaded[id] = def;
        }

        // Missing ids already warned inside TryLoad; pass empty so we do not double-log.
        return new SeasonalEventHost(loaded, missingIds: [], warn);
    }

    public static SeasonalEventHost FromDefinitions(
        IEnumerable<SeasonalEventDefinition> defs,
        IEnumerable<string>? missingIds = null,
        Action<string>? warn = null) =>
        new(defs.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase), missingIds, warn);
}
