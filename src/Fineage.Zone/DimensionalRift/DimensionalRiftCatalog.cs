using System.Text.Json;

namespace Fineage.Zone.DimensionalRift;

public sealed record DimensionalRiftPoint(int X, int Y, int Z);

public sealed record DimensionalRiftRoom(
    int Type,
    int Room,
    int XMin,
    int XMax,
    int YMin,
    int YMax,
    int ZMin,
    int ZMax,
    DimensionalRiftPoint Teleport,
    bool Boss);

public sealed record DimensionalRiftSpawnRow(
    int Type,
    int Room,
    int MobId,
    int Count,
    int RespawnDelaySeconds);

/// <summary>Room bounds and spawn templates from <c>data/dimensional-rift.json</c>.</summary>
public sealed class DimensionalRiftCatalog
{
    public const string FileName = "dimensional-rift.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<(int Type, int Room), DimensionalRiftRoom> _rooms = [];
    readonly List<DimensionalRiftSpawnRow> _spawns = [];

    DimensionalRiftCatalog(IEnumerable<DimensionalRiftRoom> rooms, IEnumerable<DimensionalRiftSpawnRow> spawns)
    {
        foreach (var room in rooms)
        {
            if (!_rooms.TryAdd((room.Type, room.Room), room))
                throw new InvalidDataException($"Duplicate rift room type={room.Type} room={room.Room}.");
        }

        _spawns.AddRange(spawns);
    }

    public int FragmentItemId { get; private init; } = DimensionalRiftSettings.FragmentItemId;

    public IReadOnlyList<DimensionalRiftSpawnRow> Spawns => _spawns;

    public bool TryGetRoom(int type, int room, out DimensionalRiftRoom site) =>
        _rooms.TryGetValue((type, room), out site!);

    public DimensionalRiftRoom Room(int type, int room) =>
        _rooms.TryGetValue((type, room), out var site)
            ? site
            : throw new KeyNotFoundException($"rift room type={type} room={room}");

    public IEnumerable<DimensionalRiftRoom> RoomsOfType(int type) =>
        _rooms.Values.Where(r => r.Type == type);

    public IEnumerable<DimensionalRiftSpawnRow> SpawnsInRoom(int type, int room) =>
        _spawns.Where(s => s.Type == type && s.Room == room);

    public bool Contains(int x, int y, int z, int type, int room)
    {
        if (!TryGetRoom(type, room, out var site))
            return false;
        return x >= site.XMin && x <= site.XMax && y >= site.YMin && y <= site.YMax && z >= site.ZMin && z <= site.ZMax;
    }

    public (int X, int Y) RandomXY(DimensionalRiftRoom room, Random rng)
    {
        int xMin = room.XMin + 128;
        int xMax = room.XMax - 128;
        int yMin = room.YMin + 128;
        int yMax = room.YMax - 128;
        if (xMax < xMin) xMax = xMin;
        if (yMax < yMin) yMax = yMin;
        return (rng.Next(xMin, xMax + 1), rng.Next(yMin, yMax + 1));
    }

    public static DimensionalRiftCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<RiftFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Dimensional rift catalog {path} was empty.");
        if (file.Version != 1)
            throw new InvalidDataException($"Dimensional rift catalog version {file.Version} is not supported.");

        var rooms = new List<DimensionalRiftRoom>();
        foreach (var row in file.Rooms ?? [])
        {
            if (row.Teleport == null)
                throw new InvalidDataException($"Room type={row.Type} room={row.Room} missing teleport.");
            rooms.Add(new DimensionalRiftRoom(
                row.Type, row.Room, row.XMin, row.XMax, row.YMin, row.YMax, row.ZMin, row.ZMax,
                new DimensionalRiftPoint(row.Teleport.X, row.Teleport.Y, row.Teleport.Z), row.Boss));
        }

        var spawns = new List<DimensionalRiftSpawnRow>();
        foreach (var row in file.Spawns ?? [])
            spawns.Add(new DimensionalRiftSpawnRow(row.Type, row.Room, row.MobId, row.Count, row.RespawnDelaySeconds));

        var catalog = new DimensionalRiftCatalog(rooms, spawns)
        {
            FragmentItemId = file.FragmentItemId > 0 ? file.FragmentItemId : DimensionalRiftSettings.FragmentItemId
        };
        return catalog;
    }

    public static DimensionalRiftCatalog LoadDefault()
    {
        string? directory = FindDataDirectory(AppContext.BaseDirectory)
            ?? FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new DirectoryNotFoundException("Could not find data/dimensional-rift.json.");
        return Load(Path.Combine(directory, FileName));
    }

    static string? FindDataDirectory(string start)
    {
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

    sealed class RiftFile
    {
        public int Version { get; set; }
        public int FragmentItemId { get; set; }
        public List<RoomRow>? Rooms { get; set; }
        public List<SpawnRow>? Spawns { get; set; }
    }

    sealed class RoomRow
    {
        public int Type { get; set; }
        public int Room { get; set; }
        public int XMin { get; set; }
        public int XMax { get; set; }
        public int YMin { get; set; }
        public int YMax { get; set; }
        public int ZMin { get; set; }
        public int ZMax { get; set; }
        public PointRow? Teleport { get; set; }
        public bool Boss { get; set; }
    }

    sealed class PointRow
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    sealed class SpawnRow
    {
        public int Type { get; set; }
        public int Room { get; set; }
        public int MobId { get; set; }
        public int Count { get; set; }
        public int RespawnDelaySeconds { get; set; }
    }
}