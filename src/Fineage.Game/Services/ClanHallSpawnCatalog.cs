using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>One Mobius owner_restart spawn inside a clan hall (<c>zones/clan_hall.xml</c>).</summary>
public sealed record ClanHallSpawnPoint(int X, int Y, int Z);

/// <summary>Teleport + regen footprint for one clan hall (<c>data/clan-hall-spawns.json</c>).</summary>
public sealed record ClanHallSpawn(
    int HallId,
    ClanHallSpawnPoint Owner,
    IReadOnlyList<ClanHallSpawnPoint> Chaotic,
    int CentroidX,
    int CentroidY,
    int Radius)
{
    public bool Contains(int x, int y)
    {
        long dx = x - (long)CentroidX;
        long dy = y - (long)CentroidY;
        long limit = (long)Radius * Radius;
        return dx * dx + dy * dy <= limit;
    }

    /// <summary>Mobius <c>getSpawnLoc</c> / chaotic ring when the player has karma.</summary>
    public ClanHallSpawnPoint Resolve(bool chaotic, Random rng)
    {
        if (!chaotic || Chaotic.Count == 0)
            return Owner;
        if (Chaotic.Count == 1)
            return Chaotic[0];
        return Chaotic[rng.Next(Chaotic.Count)];
    }
}

/// <summary>Mobius clan-hall escape and death restart points.</summary>
public sealed class ClanHallSpawnCatalog
{
    public const string FileName = "clan-hall-spawns.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, ClanHallSpawn> _halls;

    ClanHallSpawnCatalog(Dictionary<int, ClanHallSpawn> halls) => _halls = halls;

    public static readonly ClanHallSpawnCatalog Empty = new([]);

    public bool TryGet(int hallId, out ClanHallSpawn spawn) => _halls.TryGetValue(hallId, out spawn!);

    public bool HasSpawn(int hallId) => hallId > 0 && _halls.ContainsKey(hallId);

    public bool TryResolve(int hallId, bool chaotic, Random rng, out ClanHallSpawnPoint point)
    {
        point = default!;
        if (!_halls.TryGetValue(hallId, out var spawn))
            return false;
        point = spawn.Resolve(chaotic, rng);
        return true;
    }

    public static ClanHallSpawnCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static ClanHallSpawnCatalog Parse(string json, string source = FileName)
    {
        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not a clan hall spawn table.", ex);
        }

        var halls = new Dictionary<int, ClanHallSpawn>();
        foreach (var row in file?.Halls ?? [])
        {
            if (row.HallId <= 0)
                continue;
            var owner = PointOf(row.X, row.Y, row.Z);
            var chaotic = ParseChaotic(row.Chaotic);
            int cx = row.Centroid?.X ?? row.X;
            int cy = row.Centroid?.Y ?? row.Y;
            int radius = row.Radius > 0 ? row.Radius : file?.DefaultRadius ?? 1200;
            halls[row.HallId] = new ClanHallSpawn(row.HallId, owner, chaotic, cx, cy, radius);
        }

        return new ClanHallSpawnCatalog(halls);
    }

    static ClanHallSpawnPoint PointOf(int x, int y, int z) => new(x, y, z);

    static List<ClanHallSpawnPoint> ParseChaotic(ChaoticDto? chaotic)
    {
        if (chaotic == null)
            return [];
        if (chaotic.X != 0 || chaotic.Y != 0 || chaotic.Z != 0)
            return [PointOf(chaotic.X, chaotic.Y, chaotic.Z)];
        var list = new List<ClanHallSpawnPoint>();
        foreach (var p in chaotic.Points ?? [])
            list.Add(PointOf(p.X, p.Y, p.Z));
        return list;
    }

    sealed class FileDto
    {
        public int DefaultRadius { get; set; } = 1200;
        public List<HallDto>? Halls { get; set; }
    }

    sealed class HallDto
    {
        public int HallId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Radius { get; set; }
        public CentroidDto? Centroid { get; set; }
        public ChaoticDto? Chaotic { get; set; }
    }

    sealed class CentroidDto
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    sealed class ChaoticDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public List<PointDto>? Points { get; set; }
    }

    sealed class PointDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }
}