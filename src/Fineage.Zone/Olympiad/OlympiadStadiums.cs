using System.Text.Json;

namespace Fineage.Zone.Olympiad;

/// <summary>
/// One Interlude stadium. The center is the Mobius observer coordinate.
/// Fighters stand <see cref="Fineage.Protocol.OlympiadProtocol.SpawnSeparation"/> east and west of it.
/// The polygon is the Olympiad stadium zone.
/// </summary>
public sealed class OlympiadStadium
{
    public OlympiadStadium(int id, int zoneId, string name, int x, int y, int z, int minZ, int maxZ, IReadOnlyList<OlympiadNode> nodes)
    {
        Id = id;
        ZoneId = zoneId;
        Name = name;
        X = x;
        Y = y;
        Z = z;
        MinZ = minZ;
        MaxZ = maxZ;
        Nodes = nodes;
    }

    public int Id { get; }
    public int ZoneId { get; }
    public string Name { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public int MinZ { get; }
    public int MaxZ { get; }
    public IReadOnlyList<OlympiadNode> Nodes { get; }

    public bool Contains(int x, int y, int z)
    {
        if (z < MinZ || z > MaxZ || Nodes.Count < 3)
            return false;
        bool inside = false;
        for (int i = 0, j = Nodes.Count - 1; i < Nodes.Count; j = i++)
        {
            int yi = Nodes[i].Y;
            int yj = Nodes[j].Y;
            if ((yi > y) == (yj > y))
                continue;
            double cross = (Nodes[j].X - (double)Nodes[i].X) * (y - yi) / (yj - yi) + Nodes[i].X;
            if (x < cross)
                inside = !inside;
        }

        return inside;
    }
}

public readonly record struct OlympiadNode(int X, int Y);

/// <summary>Loads <c>data/olympiad-stadiums.json</c>. Coordinates are ported; the file is not a Mobius XML copy.</summary>
public sealed class OlympiadStadiumCatalog
{
    public const string FileName = "olympiad-stadiums.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly OlympiadStadium[] _stadiums;

    public OlympiadStadiumCatalog(IReadOnlyList<OlympiadStadium> stadiums)
    {
        ArgumentNullException.ThrowIfNull(stadiums);
        if (stadiums.Count == 0)
            throw new InvalidDataException("Olympiad stadium catalog is empty.");
        _stadiums = new OlympiadStadium[stadiums.Count];
        for (int i = 0; i < stadiums.Count; i++)
        {
            var site = stadiums[i];
            if (site.Id != i)
                throw new InvalidDataException($"Olympiad stadium {site.Id} is out of order.");
            if (site.ZoneId <= 0 || string.IsNullOrWhiteSpace(site.Name))
                throw new InvalidDataException($"Olympiad stadium {i} needs a zone id and a name.");
            _stadiums[i] = site;
        }
    }

    public int Count => _stadiums.Length;

    public OlympiadStadium this[int index] => _stadiums[index];

    public IReadOnlyList<OlympiadStadium> All => _stadiums;

    public static OlympiadStadiumCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Olympiad stadium catalog was not found.", path);
        var file = JsonSerializer.Deserialize<StadiumFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Olympiad stadium catalog {path} was empty.");
        if (file.Version != 1)
            throw new InvalidDataException($"Olympiad stadium catalog {path} version {file.Version} is not supported.");
        if (file.Stadiums == null || file.Stadiums.Count == 0)
            throw new InvalidDataException($"Olympiad stadium catalog {path} has no stadiums.");

        var sites = new List<OlympiadStadium>(file.Stadiums.Count);
        foreach (var row in file.Stadiums)
        {
            if (row.Nodes == null || row.Nodes.Count < 3)
                throw new InvalidDataException($"Olympiad stadium {row.Id} needs a polygon.");
            var nodes = new List<OlympiadNode>(row.Nodes.Count);
            foreach (var node in row.Nodes)
                nodes.Add(new OlympiadNode(node.X, node.Y));
            sites.Add(new OlympiadStadium(row.Id, row.ZoneId, row.Name ?? "", row.X, row.Y, row.Z, row.MinZ, row.MaxZ, nodes));
        }

        return new OlympiadStadiumCatalog(sites);
    }

    public static OlympiadStadiumCatalog LoadDefault()
    {
        string? directory = FindDataDirectory(AppContext.BaseDirectory)
            ?? FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new DirectoryNotFoundException("Could not find data/olympiad-stadiums.json.");
        return Load(Path.Combine(directory, FileName));
    }

    static string? FindDataDirectory(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", FileName)))
                return Path.Combine(dir.FullName, "data");
            dir = dir.Parent;
        }

        return null;
    }

    sealed class StadiumFile
    {
        public int Version { get; set; }
        public List<StadiumRow>? Stadiums { get; set; }
    }

    sealed class StadiumRow
    {
        public int Id { get; set; }
        public int ZoneId { get; set; }
        public string? Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int MinZ { get; set; }
        public int MaxZ { get; set; }
        public List<NodeRow>? Nodes { get; set; }
    }

    sealed class NodeRow
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
