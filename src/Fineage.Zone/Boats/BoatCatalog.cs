using System.Text.Json;

namespace Fineage.Zone.Boats;

/// <summary>Mobius vehicle scripts → <c>data/boats.json</c> (<c>import_boats.py</c>).</summary>
public sealed class BoatCatalog
{
    public const string FileName = "boats.json";
    public static readonly BoatCatalog Empty = new([]);

    readonly BoatDefinition[] _boats;
    readonly Dictionary<int, BoatDefinition> _byId = [];

    public BoatCatalog(BoatDefinition[] boats)
    {
        _boats = boats;
        foreach (var b in boats)
            _byId[b.Id] = b;
    }

    public int Count => _boats.Length;
    public IReadOnlyList<BoatDefinition> All => _boats;
    public BoatDefinition? Get(int id) => _byId.TryGetValue(id, out var b) ? b : null;

    public static BoatCatalog LoadDefault()
    {
        string? path = FindData(FileName);
        if (path == null)
        {
            ZoneLog.Info("[boat] data/boats.json not found");
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<BoatDefinition>();
            foreach (var b in doc.RootElement.EnumerateArray())
            {
                var spawn = b.GetProperty("spawn");
                var docks = ParseDocks(b);
                var legs = ParseLegs(b);
                list.Add(new BoatDefinition
                {
                    Id = b.GetProperty("id").GetInt32(),
                    BoatNpcId = b.GetProperty("boatNpcId").GetInt32(),
                    SpawnX = spawn.GetProperty("x").GetInt32(),
                    SpawnY = spawn.GetProperty("y").GetInt32(),
                    SpawnZ = spawn.GetProperty("z").GetInt32(),
                    SpawnHeading = spawn.GetProperty("heading").GetInt32(),
                    Docks = docks,
                    Legs = legs
                });
            }
            ZoneLog.Info($"[boat] loaded {list.Count} boat route(s) from {path}");
            return new BoatCatalog([.. list]);
        }
        catch (Exception ex)
        {
            ZoneLog.Info($"[boat] failed to load {path}: {ex.Message}");
            return Empty;
        }
    }

    static BoatDock[] ParseDocks(JsonElement b)
    {
        if (!b.TryGetProperty("docks", out var arr))
            return [];
        var list = new List<BoatDock>();
        foreach (var d in arr.EnumerateArray())
        {
            list.Add(new BoatDock
            {
                HarborId = d.GetProperty("harborId").GetInt32(),
                WaitMs = d.TryGetProperty("waitMs", out var w) ? w.GetInt32() : 0,
                Points = ParsePoints(d.GetProperty("points"))
            });
        }
        return [.. list];
    }

    static BoatLeg[] ParseLegs(JsonElement b)
    {
        var list = new List<BoatLeg>();
        foreach (var leg in b.GetProperty("legs").EnumerateArray())
            list.Add(new BoatLeg { Points = ParsePoints(leg.GetProperty("points")) });
        return [.. list];
    }

    static BoatPathPoint[] ParsePoints(JsonElement arr)
    {
        var list = new List<BoatPathPoint>();
        foreach (var p in arr.EnumerateArray())
        {
            list.Add(new BoatPathPoint
            {
                X = p.GetProperty("x").GetInt32(),
                Y = p.GetProperty("y").GetInt32(),
                Z = p.GetProperty("z").GetInt32(),
                Heading = p.TryGetProperty("heading", out var h) ? h.GetInt32() : 0,
                Speed = p.TryGetProperty("speed", out var s) ? s.GetInt32() : 800
            });
        }
        return [.. list];
    }

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

public sealed class BoatDefinition
{
    public int Id { get; init; }
    public int BoatNpcId { get; init; }
    public int SpawnX { get; init; }
    public int SpawnY { get; init; }
    public int SpawnZ { get; init; }
    public int SpawnHeading { get; init; }
    public BoatDock[] Docks { get; init; } = [];
    public BoatLeg[] Legs { get; init; } = [];
}

public sealed class BoatDock
{
    public int HarborId { get; init; }
    public int WaitMs { get; init; }
    public BoatPathPoint[] Points { get; init; } = [];
}

public sealed class BoatLeg
{
    public BoatPathPoint[] Points { get; init; } = [];
}

public sealed class BoatPathPoint
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int Heading { get; init; }
    public int Speed { get; init; } = 800;
}

public static class BoatSettings
{
    public static bool Enabled => ReadBool("FINEAGE_ALLOW_BOATS", defaultValue: true);

    static bool ReadBool(string name, bool defaultValue)
    {
        string? v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v))
            return defaultValue;
        return v is "1" or "true" or "TRUE" or "yes";
    }
}