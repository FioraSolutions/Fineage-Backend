using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Siege;

/// <summary>One castle gate or wall (Mobius <c>Doors.xml</c> castle rows): base HP, defences, wall flag.</summary>
public sealed record CastleDoorRow(int Id, string Name, int Hp, int PDef, int MDef, bool Wall)
{
    /// <summary>Mobius chamberlain upgrade type: 1 outer gate, 2 inner gate, 3 wall.</summary>
    public int UpgradeType => Wall ? 3 : Name.Contains("_outter_", StringComparison.Ordinal) ? 1 : 2;
}

/// <summary>A placed siege NPC: control / flame tower or a default guard (<c>castle_siege_guards</c> isHired = 0).</summary>
public sealed record CastlePlacement(int NpcId, int X, int Y, int Z, int Heading = 0, int RespawnSeconds = 0);

public readonly record struct CastlePoint(int X, int Y, int Z);

/// <summary>Mobius <c>HqZone</c> polygon (<c>castle_hq.xml</c>): where an attacker leader may build headquarters.</summary>
public sealed record CastlePolygon(int MinZ, int MaxZ, IReadOnlyList<(int X, int Y)> Nodes)
{
    public bool Contains(int x, int y, int z)
    {
        if (z < MinZ || z > MaxZ || Nodes.Count < 3)
            return false;
        bool inside = false;
        for (int i = 0, j = Nodes.Count - 1; i < Nodes.Count; j = i++)
        {
            var (xi, yi) = Nodes[i];
            var (xj, yj) = Nodes[j];
            if ((yi > y) != (yj > y) && x < (double)(xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }
}

/// <summary>Siege data for one castle, from <c>data/castles.json</c> (tools/import_castles.py).</summary>
public sealed record CastleDataRow(
    int Id,
    string Name,
    IReadOnlyList<int> ArtifactNpcIds,
    IReadOnlyList<CastlePlacement> ControlTowers,
    IReadOnlyList<CastlePlacement> FlameTowers,
    int MaxMercenaries,
    IReadOnlyList<CastleDoorRow> Doors,
    IReadOnlyList<CastlePoint> OwnerRestart,
    IReadOnlyList<CastlePlacement> Guards,
    IReadOnlyList<CastlePolygon> HqZones)
{
    public bool InHqZone(int x, int y, int z) => HqZones.Any(zone => zone.Contains(x, y, z));
}

/// <summary>data/castles.json: artifacts, towers, gates / walls, default guards, owner restart points.</summary>
public sealed class CastleData
{
    public const string FileName = "castles.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    static readonly Lazy<CastleData> Shared = new(Load);

    readonly Dictionary<int, CastleDataRow> _byId = [];
    readonly Dictionary<int, int> _artifactCastle = [];
    readonly Dictionary<int, int> _doorCastle = [];

    public CastleData(IEnumerable<CastleDataRow> rows)
    {
        foreach (var row in rows)
        {
            _byId[row.Id] = row;
            foreach (int npcId in row.ArtifactNpcIds)
                _artifactCastle[npcId] = row.Id;
            foreach (var door in row.Doors)
                _doorCastle[door.Id] = row.Id;
        }
    }

    public static readonly CastleData Empty = new([]);

    public static CastleData Default => Shared.Value;

    public IEnumerable<CastleDataRow> All => _byId.Values.OrderBy(row => row.Id);

    public CastleDataRow? Find(int castleId) => _byId.TryGetValue(castleId, out var row) ? row : null;

    /// <summary>Castle whose holy artifact this npc id is, or 0.</summary>
    public int ArtifactCastle(int npcId) => _artifactCastle.TryGetValue(npcId, out int id) ? id : 0;

    public int DoorCastle(int doorId) => _doorCastle.TryGetValue(doorId, out int id) ? id : 0;

    static CastleData Load()
    {
        string? path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static CastleData Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? new FileDto();
        var rows = new List<CastleDataRow>();
        foreach (var c in file.Castles ?? [])
        {
            if (c.Id <= 0)
                continue;
            rows.Add(new CastleDataRow(
                c.Id,
                c.Name ?? "",
                c.ArtifactNpcIds ?? [],
                Placements(c.ControlTowers),
                Placements(c.FlameTowers),
                c.MaxMercenaries,
                (c.Doors ?? []).Where(d => d.Id > 0 && d.Hp > 0)
                    .Select(d => new CastleDoorRow(d.Id, d.Name ?? "", d.Hp, d.PDef, d.MDef, d.Wall)).ToArray(),
                (c.OwnerRestart ?? []).Select(p => new CastlePoint(p.X, p.Y, p.Z)).ToArray(),
                Placements(c.Guards),
                (c.HqZones ?? []).Select(z => new CastlePolygon(
                    z.MinZ, z.MaxZ, (z.Nodes ?? []).Where(n => n.Length >= 2).Select(n => (n[0], n[1])).ToArray())).ToArray()));
        }

        return new CastleData(rows);
    }

    static CastlePlacement[] Placements(List<PlacementDto>? rows) =>
        (rows ?? []).Where(p => p.NpcId > 0)
            .Select(p => new CastlePlacement(p.NpcId, p.X, p.Y, p.Z, p.Heading, p.RespawnSeconds)).ToArray();

    sealed class FileDto
    {
        public List<CastleDto>? Castles { get; set; }
    }

    sealed class CastleDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int[]? ArtifactNpcIds { get; set; }
        public List<PlacementDto>? ControlTowers { get; set; }
        public List<PlacementDto>? FlameTowers { get; set; }
        public int MaxMercenaries { get; set; }
        public List<DoorDto>? Doors { get; set; }
        public List<PointDto>? OwnerRestart { get; set; }
        public List<PlacementDto>? Guards { get; set; }
        public List<PolygonDto>? HqZones { get; set; }
    }

    sealed class PolygonDto
    {
        public int MinZ { get; set; }
        public int MaxZ { get; set; }
        public List<int[]>? Nodes { get; set; }
    }

    sealed class PlacementDto
    {
        public int NpcId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
        public int RespawnSeconds { get; set; }
    }

    sealed class DoorDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Hp { get; set; }
        public int PDef { get; set; }
        public int MDef { get; set; }
        public bool Wall { get; set; }
    }

    sealed class PointDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }
}
