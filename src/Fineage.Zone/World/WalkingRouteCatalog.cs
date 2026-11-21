using System.Text.Json;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>Routes.xml</c> → <c>data/walking-routes.json</c> (import_walking_routes.py).</summary>
public sealed class WalkingRouteCatalog
{
    public const string FileName = "walking-routes.json";
    public static readonly WalkingRouteCatalog Empty = new([]);

    readonly WalkingRoute[] _routes;
    readonly Dictionary<string, WalkingRoute> _byName = [];
    readonly Dictionary<int, List<WalkingRoute>> _byNpcId = [];

    public WalkingRouteCatalog(WalkingRoute[] routes)
    {
        _routes = routes;
        foreach (var route in routes)
        {
            if (!string.IsNullOrEmpty(route.Name))
                _byName[route.Name] = route;
            foreach (var t in route.Targets)
            {
                if (!_byNpcId.TryGetValue(t.NpcId, out var list))
                {
                    list = [];
                    _byNpcId[t.NpcId] = list;
                }
                list.Add(route);
            }
        }
    }

    public int Count => _routes.Length;

    public WalkingRoute? GetByName(string name) =>
        _byName.TryGetValue(name, out var route) ? route : null;

    /// <summary>First route whose target matches npc id and spawn within 256 units (Mobius attach).</summary>
    public WalkingRoute? Match(int npcId, int x, int y, int z)
    {
        if (!_byNpcId.TryGetValue(npcId, out var list))
            return null;
        const int radiusSq = 256 * 256;
        foreach (var route in list)
        {
            foreach (var t in route.Targets)
            {
                if (t.NpcId != npcId)
                    continue;
                long dx = x - t.SpawnX;
                long dy = y - t.SpawnY;
                long dz = z - t.SpawnZ;
                if (dx * dx + dy * dy + dz * dz <= radiusSq)
                    return route;
            }
        }
        return null;
    }

    public static WalkingRouteCatalog LoadDefault()
    {
        string? path = FindData(FileName);
        if (path == null)
        {
            ZoneLog.Info("[walking] data/walking-routes.json not found");
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<WalkingRoute>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var points = new List<WalkingPoint>();
                foreach (var p in r.GetProperty("points").EnumerateArray())
                {
                    points.Add(new WalkingPoint
                    {
                        X = p.GetProperty("x").GetInt32(),
                        Y = p.GetProperty("y").GetInt32(),
                        Z = p.GetProperty("z").GetInt32(),
                        DelaySec = p.TryGetProperty("delaySec", out var d) ? d.GetInt32() : 0,
                        Run = !p.TryGetProperty("run", out var run) || run.GetBoolean(),
                        Chat = p.TryGetProperty("chat", out var c) ? c.GetString() : null
                    });
                }
                if (points.Count == 0)
                    continue;
                var targets = new List<WalkingTarget>();
                foreach (var t in r.GetProperty("targets").EnumerateArray())
                {
                    targets.Add(new WalkingTarget
                    {
                        NpcId = t.GetProperty("npcId").GetInt32(),
                        SpawnX = t.GetProperty("spawnX").GetInt32(),
                        SpawnY = t.GetProperty("spawnY").GetInt32(),
                        SpawnZ = t.GetProperty("spawnZ").GetInt32()
                    });
                }
                list.Add(new WalkingRoute
                {
                    Name = r.GetProperty("name").GetString() ?? "",
                    Repeat = !r.TryGetProperty("repeat", out var rep) || rep.GetBoolean(),
                    Style = ParseStyle(r.GetProperty("repeatStyle").GetString()),
                    Targets = [.. targets],
                    Points = [.. points]
                });
            }
            ZoneLog.Info($"[walking] loaded {list.Count} route(s) from {path}");
            return new WalkingRouteCatalog([.. list]);
        }
        catch (Exception ex)
        {
            ZoneLog.Info($"[walking] failed to load {path}: {ex.Message}");
            return Empty;
        }
    }

    static WalkingRepeatStyle ParseStyle(string? text) => text?.ToLowerInvariant() switch
    {
        "back" => WalkingRepeatStyle.Back,
        "conveyor" => WalkingRepeatStyle.Conveyor,
        "random" => WalkingRepeatStyle.Random,
        _ => WalkingRepeatStyle.Cycle
    };

    static string? FindData(string fileName)
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var d = new DirectoryInfo(Path.GetFullPath(start));
            while (d != null)
            {
                string p = Path.Combine(d.FullName, "data", fileName);
                if (File.Exists(p))
                    return p;
                d = d.Parent;
            }
        }
        return null;
    }
}

public enum WalkingRepeatStyle : byte
{
    Back,
    Cycle,
    Conveyor,
    Random
}

public sealed class WalkingRoute
{
    public string Name { get; init; } = "";
    public bool Repeat { get; init; } = true;
    public WalkingRepeatStyle Style { get; init; } = WalkingRepeatStyle.Cycle;
    public WalkingTarget[] Targets { get; init; } = [];
    public WalkingPoint[] Points { get; init; } = [];
}

public sealed class WalkingTarget
{
    public int NpcId { get; init; }
    public int SpawnX { get; init; }
    public int SpawnY { get; init; }
    public int SpawnZ { get; init; }
}

public sealed class WalkingPoint
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int DelaySec { get; init; }
    public bool Run { get; init; } = true;
    public string? Chat { get; init; }
}