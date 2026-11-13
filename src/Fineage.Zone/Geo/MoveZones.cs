using System.Text.Json;

namespace Fineage.Zone.Geo;

/// <summary>One Mobius door: 4-node footprint polygon, vertical span nodeZ..nodeZ+height, open/closed state.</summary>
public sealed class DoorInstance
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public (int X, int Y)[] Nodes { get; init; } = [];
    public int MinZ { get; init; }
    public int MaxZ { get; init; }
    public bool Open { get; set; }
    public int MinX { get; init; }
    public int MaxX { get; init; }
    public int MinY { get; init; }
    public int MaxY { get; init; }
}

/// <summary>
/// Mobius <c>DoorData</c> (movement part only): doors from <c>data/doors.json</c> (converted from Doors.xml by
/// <c>tools/import_doors_water.py</c>). A closed door blocks a move segment that crosses its footprint within its Z span
/// (Mobius <c>checkIfDoorsBetween</c>, called from <c>GeoEngine.canMoveToTarget</c>). Open/close is server state only:
/// there is no Net door entity, doorman/siege/timer opening yet — doors keep their datapack default status.
/// </summary>
public sealed class DoorTable
{
    public const string FileName = "doors.json";
    public static readonly DoorTable Empty = new([]);

    readonly List<DoorInstance> _doors;
    readonly Dictionary<int, DoorInstance> _byId = [];

    public DoorTable(List<DoorInstance> doors)
    {
        _doors = doors;
        foreach (var d in doors)
            _byId[d.Id] = d;
    }

    public int Count => _doors.Count;
    public IReadOnlyList<DoorInstance> All => _doors;
    public DoorInstance? Get(int id) => _byId.TryGetValue(id, out var d) ? d : null;

    public static DoorTable LoadDefault()
    {
        string? path = MoveZoneFiles.Find(FileName);
        if (path == null)
        {
            ZoneLog.Info("[door] data/doors.json not found -> no door collision");
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<DoorInstance>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var nodes = r.GetProperty("nodes").EnumerateArray()
                    .Select(n => (n[0].GetInt32(), n[1].GetInt32())).ToArray();
                int nodeZ = r.GetProperty("nodeZ").GetInt32();
                int height = r.GetProperty("height").GetInt32();
                list.Add(new DoorInstance
                {
                    Id = r.GetProperty("id").GetInt32(),
                    Name = r.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                    X = r.GetProperty("x").GetInt32(),
                    Y = r.GetProperty("y").GetInt32(),
                    Z = r.GetProperty("z").GetInt32(),
                    Nodes = nodes,
                    MinZ = nodeZ,
                    MaxZ = nodeZ + height,
                    Open = r.TryGetProperty("open", out var o) && o.GetBoolean(),
                    MinX = nodes.Min(n => n.Item1),
                    MaxX = nodes.Max(n => n.Item1),
                    MinY = nodes.Min(n => n.Item2),
                    MaxY = nodes.Max(n => n.Item2)
                });
            }

            ZoneLog.Info($"[door] loaded {list.Count} door(s) from {path} ({list.Count(d => !d.Open)} closed by default)");
            return new DoorTable(list);
        }
        catch (Exception ex)
        {
            ZoneLog.Info($"[door] failed to load {path}: {ex.Message} -> no door collision");
            return Empty;
        }
    }

    /// <summary>
    /// First closed door crossed by segment (ox,oy,oz)→(tx,ty,tz). Returns the door and the stop point a little before
    /// the crossing; null when the path is clear.
    /// </summary>
    public DoorInstance? FirstBlocking(int ox, int oy, int oz, int tx, int ty, int tz, out int stopX, out int stopY, out int stopZ)
    {
        stopX = tx; stopY = ty; stopZ = tz;
        if (_doors.Count == 0 || (ox == tx && oy == ty))
            return null;
        int sMinX = Math.Min(ox, tx), sMaxX = Math.Max(ox, tx), sMinY = Math.Min(oy, ty), sMaxY = Math.Max(oy, ty);
        int sMinZ = Math.Min(oz, tz), sMaxZ = Math.Max(oz, tz);
        DoorInstance? best = null;
        double bestT = double.MaxValue;
        foreach (var d in _doors)
        {
            if (d.Open || d.MaxX < sMinX || d.MinX > sMaxX || d.MaxY < sMinY || d.MinY > sMaxY)
                continue;
            // Z overlap with a small step margin (mover Z is ground; door spans nodeZ..nodeZ+height).
            if (sMaxZ < d.MinZ - 64 || sMinZ > d.MaxZ)
                continue;
            for (int i = 0; i < d.Nodes.Length; i++)
            {
                var a = d.Nodes[i];
                var b = d.Nodes[(i + 1) % d.Nodes.Length];
                if (SegmentT(ox, oy, tx, ty, a.X, a.Y, b.X, b.Y, out double t) && t < bestT)
                {
                    bestT = t;
                    best = d;
                }
            }
        }

        if (best == null)
            return null;
        double len = Math.Sqrt((double)(tx - ox) * (tx - ox) + (double)(ty - oy) * (ty - oy));
        double back = len > 0 ? Math.Max(0, bestT - 24.0 / len) : 0;
        stopX = ox + (int)Math.Round((tx - ox) * back);
        stopY = oy + (int)Math.Round((ty - oy) * back);
        stopZ = oz + (int)Math.Round((tz - oz) * back);
        return best;
    }

    /// <summary>Intersection parameter t∈[0,1] on segment P (p0→p1) with segment Q (q0→q1).</summary>
    static bool SegmentT(double p0x, double p0y, double p1x, double p1y, double q0x, double q0y, double q1x, double q1y, out double t)
    {
        t = 0;
        double rx = p1x - p0x, ry = p1y - p0y, sx = q1x - q0x, sy = q1y - q0y;
        double den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-9)
            return false;
        double qpx = q0x - p0x, qpy = q0y - p0y;
        t = (qpx * sy - qpy * sx) / den;
        double u = (qpx * ry - qpy * rx) / den;
        return t >= 0 && t <= 1 && u >= 0 && u <= 1;
    }
}

/// <summary>
/// Mobius <c>WaterZone</c> cuboids from <c>data/water-zones.json</c>. Inside one, Mobius <c>CreatureStat.getMoveSpeed</c>
/// switches to the template swim bases (<c>baseMoveSpd/slowSwim</c>, <c>fastSwim</c>) instead of walk/run; the folded
/// <c>runSpd</c> stat still applies there, so buffs and DEX change swim speed too. Breath / drowning and 3-D swim
/// (free Z) are not modelled.
/// </summary>
public sealed class WaterZones
{
    public const string FileName = "water-zones.json";
    public static readonly WaterZones Empty = new([]);

    readonly (int MinX, int MaxX, int MinY, int MaxY, int MinZ, int MaxZ)[] _zones;

    public WaterZones((int, int, int, int, int, int)[] zones) => _zones = zones;

    public int Count => _zones.Length;

    public bool IsInWater(int x, int y, int z)
    {
        foreach (var w in _zones)
        {
            if (x >= w.MinX && x <= w.MaxX && y >= w.MinY && y <= w.MaxY && z >= w.MinZ && z <= w.MaxZ)
                return true;
        }
        return false;
    }

    public static WaterZones LoadDefault()
    {
        string? path = MoveZoneFiles.Find(FileName);
        if (path == null)
        {
            ZoneLog.Info("[water] data/water-zones.json not found -> no swim speed");
            return Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<(int, int, int, int, int, int)>();
            foreach (var r in doc.RootElement.EnumerateArray())
                list.Add((r.GetProperty("minX").GetInt32(), r.GetProperty("maxX").GetInt32(),
                    r.GetProperty("minY").GetInt32(), r.GetProperty("maxY").GetInt32(),
                    r.GetProperty("minZ").GetInt32(), r.GetProperty("maxZ").GetInt32()));
            ZoneLog.Info($"[water] loaded {list.Count} water zone(s) from {path}");
            return new WaterZones([.. list]);
        }
        catch (Exception ex)
        {
            ZoneLog.Info($"[water] failed to load {path}: {ex.Message} -> no swim speed");
            return Empty;
        }
    }
}

/// <summary>
/// Mobius <c>PeaceZone</c> NPoly from <c>data/peace-zones.json</c> (zones/peace.xml and the peace rows in ssq.xml).
/// <c>Creature.isInsidePeaceZone</c>: a playable may not attack another playable when either stands inside one.
/// </summary>
public sealed class PeaceZones
{
    public const string FileName = "peace-zones.json";
    public static readonly PeaceZones Empty = new([]);

    readonly PeaceZone[] _zones;

    public PeaceZones(PeaceZone[] zones) => _zones = zones;

    public int Count => _zones.Length;

    public bool Contains(int x, int y, int z) => Hit(_zones, x, y, z);

    internal static bool Hit(PeaceZone[] zones, int x, int y, int z)
    {
        foreach (var zone in zones)
        {
            if (z < zone.MinZ || z > zone.MaxZ || x < zone.MinX || x > zone.MaxX || y < zone.MinY || y > zone.MaxY)
                continue;
            if (Inside(zone.Nodes, x, y))
                return true;
        }
        return false;
    }

    /// <summary>Even-odd ray cast. Mobius <c>ZoneForm</c> NPoly uses the same crossing test.</summary>
    static bool Inside((int X, int Y)[] poly, int x, int y)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            int xi = poly[i].X, yi = poly[i].Y, xj = poly[j].X, yj = poly[j].Y;
            if ((yi > y) != (yj > y) && x < (xj - xi) * (double)(y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    public static PeaceZones LoadDefault() => new(LoadFile(FileName, "peace"));

    internal static PeaceZone[] LoadFile(string fileName, string logTag)
    {
        string? path = MoveZoneFiles.Find(fileName);
        if (path == null)
        {
            ZoneLog.Info($"[{logTag}] data/{fileName} not found");
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var list = new List<PeaceZone>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var nodes = r.GetProperty("nodes").EnumerateArray()
                    .Select(n => (n[0].GetInt32(), n[1].GetInt32())).ToArray();
                if (nodes.Length < 3)
                    continue;
                list.Add(new PeaceZone
                {
                    Name = r.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                    MinZ = r.GetProperty("minZ").GetInt32(),
                    MaxZ = r.GetProperty("maxZ").GetInt32(),
                    Nodes = nodes,
                    MinX = nodes.Min(n => n.Item1),
                    MaxX = nodes.Max(n => n.Item1),
                    MinY = nodes.Min(n => n.Item2),
                    MaxY = nodes.Max(n => n.Item2)
                });
            }
            ZoneLog.Info($"[{logTag}] loaded {list.Count} zone(s) from {path}");
            return [.. list];
        }
        catch (Exception ex)
        {
            ZoneLog.Info($"[{logTag}] failed to load {path}: {ex.Message}");
            return [];
        }
    }
}

public sealed class PeaceZone
{
    public string Name { get; init; } = "";
    public int MinZ { get; init; }
    public int MaxZ { get; init; }
    public (int X, int Y)[] Nodes { get; init; } = [];
    public int MinX { get; init; }
    public int MaxX { get; init; }
    public int MinY { get; init; }
    public int MaxY { get; init; }
}

/// <summary>
/// Mobius <c>ArenaZone</c> from <c>data/pvp-zones.json</c> (<c>zones/pvp.xml</c>). Entering sets <c>ZoneId.PVP</c>.
/// Siege zones set that flag only while the siege is active, so they are not in this table.
/// </summary>
public sealed class PvpZones
{
    public const string FileName = "pvp-zones.json";
    public static readonly PvpZones Empty = new([]);

    readonly PeaceZone[] _zones;

    public PvpZones(PeaceZone[] zones) => _zones = zones;

    public int Count => _zones.Length;

    public bool Contains(int x, int y, int z) => PeaceZones.Hit(_zones, x, y, z);

    public static PvpZones LoadDefault() => new(PeaceZones.LoadFile(FileName, "pvp"));
}

static class MoveZoneFiles
{
    internal static string? Find(string fileName)
    {
        foreach (string start in new[] { AppContext.BaseDirectory, System.IO.Directory.GetCurrentDirectory() })
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
