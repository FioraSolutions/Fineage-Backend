using System.Text.Json;
using Fineage.Zone;
using Fineage.Zone.Geo;

namespace Fineage.Zone.Geo.ZoneTypes;

/// <summary>
/// One loader for Interlude zone types. Each type is a <see cref="ZoneTypeProfile"/>. An unknown type name is
/// logged and skipped. Movement catalogs (water, peace, swamp, …) stay on their own files; their profiles reject
/// a duplicate row in this file.
/// </summary>
public sealed class ZoneTypeRegistry
{
    public const string FileName = "zone-types.json";

    static readonly Lazy<ZoneTypeRegistry> Shared = new(LoadDefault);

    readonly Dictionary<string, ZoneTypeProfile> _byType;
    readonly List<string> _unknown = [];

    ZoneTypeRegistry(Dictionary<string, ZoneTypeProfile> byType) => _byType = byType;

    public static ZoneTypeRegistry Empty { get; } = new(CreateProfiles());

    public static ZoneTypeRegistry SharedDefault => Shared.Value;

    public IReadOnlyList<string> UnknownTypes => _unknown;

    public int ZoneCount
    {
        get
        {
            int n = 0;
            foreach (var profile in _byType.Values)
                n += profile.Count;
            return n;
        }
    }

    public T Profile<T>() where T : ZoneTypeProfile
    {
        foreach (var profile in _byType.Values)
        {
            if (profile is T typed)
                return typed;
        }

        throw new InvalidOperationException($"zone profile {typeof(T).Name} is not registered");
    }

    public int CountOf(string typeName) =>
        _byType.TryGetValue(typeName, out var profile) ? profile.Count : 0;

    public ZoneFlags Query(int x, int y, int z, int race)
    {
        var flags = new ZoneFlags();
        foreach (var profile in _byType.Values)
            profile.Contribute(x, y, z, race, flags);
        return flags;
    }

    public static ZoneTypeRegistry LoadDefault()
    {
        string? path = MoveZoneFiles.Find(FileName);
        if (path == null)
        {
            ZoneLog.Error($"[zone] data/{FileName} not found — rule zones not applied");
            return Empty;
        }

        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException)
        {
            ZoneLog.Error($"[zone] failed to load {path}: {ex.Message} — rule zones not applied");
            return Empty;
        }
    }

    public static ZoneTypeRegistry Parse(string json, string source = FileName)
    {
        var registry = new ZoneTypeRegistry(CreateProfiles());
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("zones", out var zones) || zones.ValueKind != JsonValueKind.Array)
        {
            ZoneLog.Error($"[zone] {source} missing zones array — rule zones not applied");
            return registry;
        }

        int applied = 0;
        foreach (var row in zones.EnumerateArray())
        {
            if (!row.TryGetProperty("type", out var typeEl) || typeEl.GetString() is not { Length: > 0 } typeName)
            {
                ZoneLog.Error($"[zone] {source} row missing type — not applied");
                continue;
            }

            string name = row.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            if (!registry._byType.TryGetValue(typeName, out var profile))
            {
                registry._unknown.Add(typeName);
                ZoneLog.Error($"[zone] unknown zone type '{typeName}' name={name} — not applied");
                continue;
            }

            if (!TryShape(row, name, out var shape))
            {
                ZoneLog.Error($"[zone] {typeName} '{name}' has no usable shape — not applied");
                continue;
            }

            profile.Accept(shape, ReadStats(row), ReadRaces(row, typeName, name), ReadSpawns(row, typeName, name));
            applied++;
        }

        ZoneLog.Info($"[zone] loaded {applied} zone row(s) from {source} ({registry.ZoneCount} applied, {registry._unknown.Count} unknown)");
        return registry;
    }

    static Dictionary<string, ZoneTypeProfile> CreateProfiles()
    {
        ZoneTypeProfile[] profiles =
        [
            new WaterZoneProfile(),
            new PeaceZoneProfile(),
            new ArenaZoneProfile(),
            new SiegeZoneProfile(),
            new SwampZoneProfile(),
            new DamageZoneProfile(),
            new NoLandingZoneProfile(),
            new MotherTreeZoneProfile(),
            new FishingZoneProfile(),
            new OlympiadStadiumZoneProfile(),
            new JailZoneProfile(),
            new HqZoneProfile(),
            new FlagPvPZoneProfile(),
            new NpcSpawnTerritoryProfile(),
            new NoStoreZoneProfile(),
            new NoSummonFriendZoneProfile(),
            new NoRestartZoneProfile(),
            new EffectZoneProfile(),
            new ScriptZoneProfile(),
            new TownZoneProfile(),
            new RespawnZoneProfile(),
            new ConditionZoneProfile(),
            new DerbyTrackZoneProfile(),
            new ResidenceTeleportZoneProfile(),
            new ResidenceHallTeleportZoneProfile(),
            new TaxZoneProfile(),
            new BossZoneProfile(),
            new NoPvPZoneProfile(),
            new CastleZoneProfile(),
            new ClanHallZoneProfile(),
            new SiegableHallZoneProfile()
        ];
        var map = new Dictionary<string, ZoneTypeProfile>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            if (!map.TryAdd(profile.TypeName, profile))
                throw new InvalidOperationException($"duplicate zone profile {profile.TypeName}");
        }

        return map;
    }

    static bool TryShape(JsonElement row, string name, out ZoneShape shape)
    {
        shape = null!;
        if (!row.TryGetProperty("shape", out var shapeEl) || shapeEl.GetString() is not { } kind)
            return false;
        if (!row.TryGetProperty("minZ", out var minEl) || !row.TryGetProperty("maxZ", out var maxEl))
            return false;
        int minZ = minEl.GetInt32();
        int maxZ = maxEl.GetInt32();
        if (minZ > maxZ)
            (minZ, maxZ) = (maxZ, minZ);
        if (!row.TryGetProperty("nodes", out var nodesEl) || nodesEl.ValueKind != JsonValueKind.Array)
            return false;
        var nodes = new List<(int X, int Y)>();
        foreach (var node in nodesEl.EnumerateArray())
        {
            if (node.GetArrayLength() < 2)
                return false;
            nodes.Add((node[0].GetInt32(), node[1].GetInt32()));
        }

        int radius = 0;
        if (kind == "Cylinder")
        {
            if (nodes.Count < 1 || !row.TryGetProperty("rad", out var radEl))
                return false;
            radius = radEl.GetInt32();
            if (radius <= 0)
                return false;
        }
        else if (kind == "Cuboid")
        {
            if (nodes.Count < 2)
                return false;
        }
        else if (kind == "NPoly")
        {
            if (nodes.Count < 3)
                return false;
        }
        else
        {
            ZoneLog.Error($"[zone] '{name}' shape '{kind}' is not applied");
            return false;
        }

        int minX = nodes.Min(n => n.X);
        int maxX = nodes.Max(n => n.X);
        int minY = nodes.Min(n => n.Y);
        int maxY = nodes.Max(n => n.Y);
        if (kind == "Cylinder")
        {
            minX = nodes[0].X - radius;
            maxX = nodes[0].X + radius;
            minY = nodes[0].Y - radius;
            maxY = nodes[0].Y + radius;
        }

        shape = new ZoneShape
        {
            Name = name,
            Id = row.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int id) ? id : 0,
            Kind = kind,
            MinZ = minZ,
            MaxZ = maxZ,
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            Radius = radius,
            Nodes = nodes.ToArray()
        };
        return true;
    }

    static Dictionary<string, string> ReadStats(JsonElement row)
    {
        var stats = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!row.TryGetProperty("stats", out var statsEl) || statsEl.ValueKind != JsonValueKind.Object)
            return stats;
        foreach (var prop in statsEl.EnumerateObject())
            stats[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
        return stats;
    }

    static List<ZoneRacePoint> ReadRaces(JsonElement row, string type, string name)
    {
        var races = new List<ZoneRacePoint>();
        if (!row.TryGetProperty("races", out var racesEl) || racesEl.ValueKind != JsonValueKind.Array)
            return races;
        foreach (var race in racesEl.EnumerateArray())
        {
            string raceName = race.TryGetProperty("race", out var r) ? r.GetString() ?? "" : "";
            string point = race.TryGetProperty("point", out var p) ? p.GetString() ?? "" : "";
            if (raceName.Length == 0 || point.Length == 0)
                ZoneLog.Error($"[zone] {type} '{name}' race point is empty — not applied");
            races.Add(new ZoneRacePoint(raceName, point));
        }

        return races;
    }

    static List<ZoneSpawn> ReadSpawns(JsonElement row, string type, string name)
    {
        var spawns = new List<ZoneSpawn>();
        if (!row.TryGetProperty("spawns", out var spawnsEl) || spawnsEl.ValueKind != JsonValueKind.Array)
            return spawns;
        foreach (var spawn in spawnsEl.EnumerateArray())
        {
            if (!spawn.TryGetProperty("x", out var x) || !spawn.TryGetProperty("y", out var y) || !spawn.TryGetProperty("z", out var z))
            {
                ZoneLog.Error($"[zone] {type} '{name}' spawn is missing x/y/z — not applied");
                continue;
            }

            spawns.Add(new ZoneSpawn(x.GetInt32(), y.GetInt32(), z.GetInt32()));
        }

        return spawns;
    }
}
