using System.Text.Json;

namespace Fineage.Zone.Dungeons;

public enum DungeonKind
{
    Necropolis = 1,
    Catacomb = 2
}

/// <summary>
/// One Interlude catacomb or necropolis. Coordinates are the Gatekeeper Ziggurat
/// teleport points (inside entrance, surface exit).
/// </summary>
public sealed record DungeonSite(
    int Id,
    DungeonKind Kind,
    string Name,
    int EntranceNpcId,
    int ExitNpcId,
    int EntranceX,
    int EntranceY,
    int EntranceZ,
    int ExitX,
    int ExitY,
    int ExitZ,
    int FeeItemId,
    int FeeCount);

/// <summary>Loads <c>data/dungeons.json</c>. Values are ported; the file is not a Mobius XML copy.</summary>
public sealed class DungeonCatalog
{
    public const string FileName = "dungeons.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, DungeonSite> _byId;

    public DungeonCatalog(IEnumerable<DungeonSite> sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        _byId = [];
        foreach (var site in sites)
        {
            if (site.Id <= 0)
                throw new InvalidDataException($"Dungeon id {site.Id} must be positive.");
            if (string.IsNullOrWhiteSpace(site.Name))
                throw new InvalidDataException($"Dungeon {site.Id} needs a name.");
            if (site.FeeCount < 0 || site.FeeItemId < 0)
                throw new InvalidDataException($"Dungeon {site.Id} has a negative fee.");
            if (site.FeeCount > 0 && site.FeeItemId == 0)
                throw new InvalidDataException($"Dungeon {site.Id} charges a fee without an item id.");
            if (!_byId.TryAdd(site.Id, site))
                throw new InvalidDataException($"Duplicate dungeon id {site.Id}.");
        }

        if (_byId.Count == 0)
            throw new InvalidDataException("Dungeon catalog is empty.");
    }

    public IReadOnlyCollection<DungeonSite> All => _byId.Values;

    public bool TryGet(int id, out DungeonSite site) => _byId.TryGetValue(id, out site!);

    public static DungeonCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Dungeon catalog was not found.", path);

        var file = JsonSerializer.Deserialize<DungeonFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Dungeon catalog {path} was empty.");
        if (file.Version != 1)
            throw new InvalidDataException($"Dungeon catalog {path} version {file.Version} is not supported.");
        if (file.Dungeons == null || file.Dungeons.Count == 0)
            throw new InvalidDataException($"Dungeon catalog {path} has no dungeons.");

        var sites = new List<DungeonSite>(file.Dungeons.Count);
        foreach (var row in file.Dungeons)
        {
            if (row.Entrance == null || row.Exit == null)
                throw new InvalidDataException($"Dungeon {row.Id} needs entrance and exit coordinates.");
            if (!TryKind(row.Kind, out var kind))
                throw new InvalidDataException($"Dungeon {row.Id} kind '{row.Kind}' is not necropolis or catacomb.");
            sites.Add(new DungeonSite(
                row.Id,
                kind,
                row.Name ?? "",
                row.EntranceNpcId,
                row.ExitNpcId,
                row.Entrance.X,
                row.Entrance.Y,
                row.Entrance.Z,
                row.Exit.X,
                row.Exit.Y,
                row.Exit.Z,
                row.FeeItemId,
                row.FeeCount));
        }

        return new DungeonCatalog(sites);
    }

    public static DungeonCatalog LoadDefault()
    {
        string? directory = FindDataDirectory(AppContext.BaseDirectory)
            ?? FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new DirectoryNotFoundException("Could not find data/dungeons.json. Pass the file to DungeonCatalog.Load.");
        return Load(Path.Combine(directory, FileName));
    }

    static bool TryKind(string? text, out DungeonKind kind)
    {
        if (string.Equals(text, "necropolis", StringComparison.OrdinalIgnoreCase))
        {
            kind = DungeonKind.Necropolis;
            return true;
        }

        if (string.Equals(text, "catacomb", StringComparison.OrdinalIgnoreCase))
        {
            kind = DungeonKind.Catacomb;
            return true;
        }

        kind = default;
        return false;
    }

    static string? FindDataDirectory(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "data", FileName);
            if (File.Exists(candidate))
                return Path.Combine(dir.FullName, "data");
            dir = dir.Parent;
        }

        return null;
    }

    sealed class DungeonFile
    {
        public int Version { get; set; }

        public List<DungeonRow>? Dungeons { get; set; }
    }

    sealed class DungeonRow
    {
        public int Id { get; set; }

        public string? Kind { get; set; }

        public string? Name { get; set; }

        public int EntranceNpcId { get; set; }

        public int ExitNpcId { get; set; }

        public DungeonPoint? Entrance { get; set; }

        public DungeonPoint? Exit { get; set; }

        public int FeeItemId { get; set; }

        public int FeeCount { get; set; }
    }

    sealed class DungeonPoint
    {
        public int X { get; set; }

        public int Y { get; set; }

        public int Z { get; set; }
    }
}
