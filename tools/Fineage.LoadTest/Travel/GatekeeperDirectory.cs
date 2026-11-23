using System.Text.Json;

namespace Fineage.LoadTest.Travel;

public readonly record struct GatekeeperSpawn(int NpcId, int X, int Y, int Z);

public readonly record struct TeleportDestination(int NpcId, string List, int Id, string Name, int X, int Y, int Z);

/// <summary>
/// Gatekeepers and their NORMAL teleport rows, read from the same datapack the zone loads
/// (<c>data/teleports.json</c> + <c>data/spawns/*.json</c>). A destination is a town when a
/// gatekeeper stands within <see cref="TownRadius"/> of it, so a travelling bot can always talk again.
/// </summary>
public sealed class GatekeeperDirectory
{
    public const int TownRadius = 3000;
    const string NormalType = "NORMAL";

    readonly Dictionary<(int NpcId, string List, int Id), TeleportDestination> _normal = new();
    readonly List<GatekeeperSpawn> _spawns = [];
    readonly HashSet<int> _npcIds = [];

    public string DataDir { get; }
    public IReadOnlyList<GatekeeperSpawn> Spawns => _spawns;
    public IReadOnlySet<int> NpcIds => _npcIds;

    GatekeeperDirectory(string dataDir) => DataDir = dataDir;

    public static GatekeeperDirectory Load(string dataDir)
    {
        string teleports = Path.Combine(dataDir, "teleports.json");
        string spawnsDir = Path.Combine(dataDir, "spawns");
        if (!File.Exists(teleports))
            throw new FileNotFoundException($"gatekeeper travel needs {teleports}", teleports);
        if (!Directory.Exists(spawnsDir))
            throw new DirectoryNotFoundException($"gatekeeper travel needs {spawnsDir}");

        var dir = new GatekeeperDirectory(dataDir);
        var normalNpcs = new HashSet<int>();
        using (var doc = JsonDocument.Parse(File.ReadAllText(teleports)))
        {
            foreach (var gk in doc.RootElement.GetProperty("gatekeepers").EnumerateArray())
            {
                int npcId = gk.GetProperty("npcId").GetInt32();
                foreach (var list in gk.GetProperty("lists").EnumerateArray())
                {
                    if (!string.Equals(list.GetProperty("type").GetString(), NormalType, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string name = list.GetProperty("name").GetString() ?? NormalType;
                    normalNpcs.Add(npcId);
                    foreach (var loc in list.GetProperty("locations").EnumerateArray())
                    {
                        int id = loc.GetProperty("id").GetInt32();
                        dir._normal[(npcId, name.ToUpperInvariant(), id)] = new TeleportDestination(
                            npcId,
                            name,
                            id,
                            loc.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            loc.GetProperty("x").GetInt32(),
                            loc.GetProperty("y").GetInt32(),
                            loc.GetProperty("z").GetInt32());
                    }
                }
            }
        }

        foreach (string path in Directory.EnumerateFiles(spawnsDir, "*.json").Order(StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("npcs", out var npcs))
                continue;
            foreach (var row in npcs.EnumerateArray())
            {
                int npcId = row.GetProperty("npcId").GetInt32();
                if (!normalNpcs.Contains(npcId))
                    continue;
                dir._spawns.Add(new GatekeeperSpawn(
                    npcId,
                    row.GetProperty("x").GetInt32(),
                    row.GetProperty("y").GetInt32(),
                    row.GetProperty("z").GetInt32()));
                dir._npcIds.Add(npcId);
            }
        }

        dir.KeepTownGatekeepers();
        if (dir._spawns.Count == 0)
            throw new InvalidDataException($"no spawned gatekeeper under {spawnsDir} has a NORMAL teleport to another town");
        return dir;
    }

    /// <summary>Gatekeepers with a NORMAL list but no town destination elsewhere (reported, never visited).</summary>
    public IReadOnlyList<GatekeeperSpawn> NonTownGatekeepers => _nonTown;

    readonly List<GatekeeperSpawn> _nonTown = [];

    void KeepTownGatekeepers()
    {
        var town = new List<GatekeeperSpawn>(_spawns);
        while (true)
        {
            var keep = town.Where(s => Destinations(s.NpcId).Any(d =>
                    town.Any(t => Within(t.X, t.Y, d.X, d.Y, TownRadius)) &&
                    !Within(d.X, d.Y, s.X, s.Y, TownRadius)))
                .ToList();
            if (keep.Count == town.Count)
                break;
            town = keep;
        }

        _nonTown.AddRange(_spawns.Except(town));
        _spawns.Clear();
        _spawns.AddRange(town);
        _npcIds.Clear();
        foreach (var s in town)
            _npcIds.Add(s.NpcId);
    }

    public IEnumerable<TeleportDestination> Destinations(int npcId) =>
        _normal.Values.Where(d => d.NpcId == npcId).OrderBy(d => d.Id);

    /// <summary>
    /// <c>LoadScenario.DataDir</c>, then <c>FINEAGE_DATA_DIR</c>, then the first <c>data/</c> with
    /// teleports.json above the working directory or the binary. Throws when none exists.
    /// </summary>
    public static string ResolveDataDir(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);
        string? env = Environment.GetEnvironmentVariable("FINEAGE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                string candidate = Path.Combine(d.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "teleports.json")) && Directory.Exists(Path.Combine(candidate, "spawns")))
                    return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "gatekeeper travel: no data/teleports.json found above the working directory or the binary; " +
            "set dataDir in the scenario or FINEAGE_DATA_DIR");
    }

    public bool TryDestination(int npcId, string list, int id, out TeleportDestination destination) =>
        _normal.TryGetValue((npcId, list.ToUpperInvariant(), id), out destination);

    public bool IsTown(int x, int y) => _spawns.Any(s => Within(s.X, s.Y, x, y, TownRadius));

    public GatekeeperSpawn Nearest(int x, int y) =>
        _spawns.MinBy(s => DistSq(s.X, s.Y, x, y));

    public static bool Within(int ax, int ay, int bx, int by, int range) => DistSq(ax, ay, bx, by) <= (long)range * range;

    public static long DistSq(int ax, int ay, int bx, int by)
    {
        long dx = ax - bx;
        long dy = ay - by;
        return dx * dx + dy * dy;
    }
}
