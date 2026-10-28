using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>One gatekeeper destination. <see cref="Id"/> is the 0-based bypass index.</summary>
public sealed record TeleportLocation(
    int Id,
    string Name,
    int X,
    int Y,
    int Z,
    int FeeItemId,
    int FeeCount,
    IReadOnlyList<int> CastleIds);

/// <summary>One named list on a gatekeeper. <see cref="Type"/> is NORMAL, OTHER, or a noblesse list.</summary>
public sealed record TeleportList(string Name, string Type, IReadOnlyList<TeleportLocation> Locations)
{
    public TeleportLocation? Find(int id)
    {
        foreach (var location in Locations)
        {
            if (location.Id == id)
                return location;
        }

        return null;
    }
}

/// <summary>Gatekeeper teleport lists from <c>data/teleports.json</c>.</summary>
public sealed class TeleportCatalog
{
    public const string FileName = "teleports.json";
    public const int AdenaItemId = 57;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, Dictionary<string, TeleportList>> _byNpc;

    TeleportCatalog(Dictionary<int, Dictionary<string, TeleportList>> byNpc)
    {
        _byNpc = byNpc;
    }

    public static TeleportCatalog Empty { get; } = new(new Dictionary<int, Dictionary<string, TeleportList>>());

    public int GatekeeperCount => _byNpc.Count;

    public TeleportList? Find(int npcId, string? listName)
    {
        if (string.IsNullOrWhiteSpace(listName) || !_byNpc.TryGetValue(npcId, out var lists))
            return null;
        foreach (var pair in lists)
        {
            if (pair.Key.Equals(listName.Trim(), StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }

        return null;
    }

    public static TeleportCatalog Parse(string json, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TeleportFileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<TeleportFileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not a teleport catalog.", ex);
        }

        if (file?.Gatekeepers == null)
            throw new InvalidDataException($"{source} has no gatekeepers.");

        var byNpc = new Dictionary<int, Dictionary<string, TeleportList>>();
        foreach (var keeper in file.Gatekeepers)
        {
            if (keeper.NpcId <= 0)
                throw new InvalidDataException($"{source} has a gatekeeper id that is not positive.");
            if (!byNpc.TryGetValue(keeper.NpcId, out var lists))
            {
                lists = new Dictionary<string, TeleportList>(StringComparer.OrdinalIgnoreCase);
                byNpc.Add(keeper.NpcId, lists);
            }

            foreach (var list in keeper.Lists ?? [])
            {
                if (string.IsNullOrWhiteSpace(list.Name) || string.IsNullOrWhiteSpace(list.Type))
                    throw new InvalidDataException($"{source} npc {keeper.NpcId} has a list without a name.");
                var locations = new List<TeleportLocation>();
                var seen = new HashSet<int>();
                foreach (var row in list.Locations ?? [])
                {
                    if (row.Id < 0 || !seen.Add(row.Id))
                        throw new InvalidDataException($"{source} npc {keeper.NpcId} list {list.Name} repeats location {row.Id}.");
                    if (row.FeeCount < 0)
                        throw new InvalidDataException($"{source} npc {keeper.NpcId} location {row.Id} has a negative fee.");
                    int feeItem = row.FeeItemId is > 0 ? row.FeeItemId.Value : AdenaItemId;
                    locations.Add(new TeleportLocation(
                        row.Id,
                        row.Name ?? "",
                        row.X,
                        row.Y,
                        row.Z,
                        feeItem,
                        row.FeeCount,
                        row.CastleIds ?? []));
                }

                lists[list.Name.Trim()] = new TeleportList(list.Name.Trim(), list.Type.Trim(), locations);
            }
        }

        return new TeleportCatalog(byNpc);
    }

    public static TeleportCatalog Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Teleport catalog not found: {path}", path);
        return Parse(File.ReadAllText(path), path);
    }

    public static TeleportCatalog LoadDefault()
    {
        var path = FindFile(FileName);
        if (path == null)
            throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static string? FindFile(string fileName)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "data", fileName);
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

    sealed class TeleportFileDto
    {
        public List<GatekeeperDto>? Gatekeepers { get; set; }
    }

    sealed class GatekeeperDto
    {
        public int NpcId { get; set; }
        public List<ListDto>? Lists { get; set; }
    }

    sealed class ListDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public List<LocationDto>? Locations { get; set; }
    }

    sealed class LocationDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int? FeeItemId { get; set; }
        public int FeeCount { get; set; }
        public List<int>? CastleIds { get; set; }
    }
}
