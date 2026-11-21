using System.Text.Json;

namespace Fineage.Zone.Punishment;

public sealed record JailLocation(int X, int Y, int Z);

public sealed class JailZonePolygon
{
    public string Name { get; init; } = "";
    public int MinZ { get; init; }
    public int MaxZ { get; init; }
    public (int X, int Y)[] Nodes { get; init; } = [];

    public bool Contains(int x, int y, int z)
    {
        if (z < MinZ || z > MaxZ)
            return false;
        return InsidePolygon(Nodes, x, y);
    }

    static bool InsidePolygon((int X, int Y)[] poly, int x, int y)
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
}

/// <summary>Mobius <c>JailZone</c> IN/OUT locations and jail polygons from <c>data/jail-zone.json</c>.</summary>
public sealed class JailZoneCatalog
{
    public const string FileName = "jail-zone.json";

    public static readonly JailZoneCatalog Empty = new(
      new JailLocation(-114356, -249645, -2984),
      new JailLocation(17836, 170178, -3507),
      []);

    public JailLocation LocationIn { get; }
    public JailLocation LocationOut { get; }
    public JailZonePolygon[] Zones { get; }

    JailZoneCatalog(JailLocation locationIn, JailLocation locationOut, JailZonePolygon[] zones)
    {
        LocationIn = locationIn;
        LocationOut = locationOut;
        Zones = zones;
    }

    public bool Contains(int x, int y, int z)
    {
        foreach (var zone in Zones)
        {
            if (zone.Contains(x, y, z))
                return true;
        }

        return false;
    }

    public static JailZoneCatalog LoadDefault()
    {
        try
        {
            return LoadFile(FindPath());
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"[jail] {FileName} unavailable: {ex.Message}; using Mobius defaults");
            return Empty;
        }
    }

    public static JailZoneCatalog LoadFile(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var inLoc = ReadLoc(root.GetProperty("locationIn"));
            var outLoc = ReadLoc(root.GetProperty("locationOut"));
            var zones = new List<JailZonePolygon>();
            foreach (var z in root.GetProperty("zones").EnumerateArray())
            {
                var nodes = z.GetProperty("nodes").EnumerateArray()
                  .Select(n => (n.GetProperty("x").GetInt32(), n.GetProperty("y").GetInt32()))
                  .ToArray();
                zones.Add(new JailZonePolygon
                {
                    Name = z.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    MinZ = z.GetProperty("minZ").GetInt32(),
                    MaxZ = z.GetProperty("maxZ").GetInt32(),
                    Nodes = nodes
                });
            }

            ZoneLog.Info($"[jail] loaded {zones.Count} zone(s) from {path}");
            return new JailZoneCatalog(inLoc, outLoc, zones.ToArray());
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"[jail] failed to load {path}: {ex.Message}; using Mobius defaults");
            return Empty;
        }
    }

    static JailLocation ReadLoc(JsonElement el) =>
      new(el.GetProperty("x").GetInt32(), el.GetProperty("y").GetInt32(), el.GetProperty("z").GetInt32());

    static string FindPath()
    {
        string rel = Path.Combine("data", FileName);
        if (File.Exists(rel))
            return Path.GetFullPath(rel);
        string? dir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, rel);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Missing {FileName}; expected under data/ or next to the zone binary.");
    }
}