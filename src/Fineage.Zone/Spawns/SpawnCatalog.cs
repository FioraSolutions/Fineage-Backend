using System.Text.Json;
using Fineage.Game;

namespace Fineage.Zone.Spawns;

/// <summary>
/// Loads Interlude-style fixed spawn rows from <c>data/spawns/*.json</c> (Mobius spawnlist subset).
/// </summary>
public sealed class SpawnCatalog
{
    public const string DefaultMapId = "ti_cedric";
    public const string SpawnsDirectoryName = "spawns";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    SpawnCatalog(IReadOnlyDictionary<string, SpawnMapDefinition> maps)
    {
        Maps = maps;
    }

    public static SpawnCatalog From(IReadOnlyDictionary<string, SpawnMapDefinition> maps) =>
        new(maps ?? throw new ArgumentNullException(nameof(maps)));

    public IReadOnlyDictionary<string, SpawnMapDefinition> Maps { get; }

    public SpawnMapDefinition GetMap(string mapId)
    {
        if (!Maps.TryGetValue(mapId, out var map))
            throw new KeyNotFoundException($"Spawn map '{mapId}' is not in the catalog.");
        return map;
    }

    public static SpawnCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var spawnsDir = Path.Combine(dataDirectory, SpawnsDirectoryName);
        if (!Directory.Exists(spawnsDir))
            throw new DirectoryNotFoundException($"Spawn data directory not found: {spawnsDir}");

        var maps = new Dictionary<string, SpawnMapDefinition>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(spawnsDir, "*.json"))
        {
            var map = ReadMap(path);
            if (maps.ContainsKey(map.MapId))
                throw new InvalidDataException($"Duplicate spawn map id '{map.MapId}' in {path}.");
            maps.Add(map.MapId, map);
        }

        if (maps.Count == 0)
            throw new InvalidDataException($"No spawn maps under {spawnsDir}.");

        return new SpawnCatalog(maps);
    }

    public static SpawnCatalog LoadDefault()
    {
        var directory = GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
        {
            throw new DirectoryNotFoundException(
                "Could not find data/spawns. Pass the data directory to SpawnCatalog.Load.");
        }

        return Load(directory);
    }

    static SpawnMapDefinition ReadMap(string path)
    {
        MapFileDto file;
        try
        {
            var json = File.ReadAllText(path);
            file = JsonSerializer.Deserialize<MapFileDto>(json, JsonOptions)
                ?? throw new InvalidDataException($"{path} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }

        if (string.IsNullOrWhiteSpace(file.MapId))
            throw new InvalidDataException($"{path} needs mapId.");
        if (file.Npcs == null || file.Npcs.Count == 0)
            throw new InvalidDataException($"{path} has no npcs.");

        var npcs = new List<SpawnNpcDefinition>(file.Npcs.Count);
        foreach (var row in file.Npcs)
        {
            if (row.ObjectId <= 0)
                throw new InvalidDataException($"{path} has a non-positive objectId.");
            if (row.NpcId <= 0)
                throw new InvalidDataException($"{path} object {row.ObjectId} needs npcId.");
            if (string.IsNullOrWhiteSpace(row.Name))
                throw new InvalidDataException($"{path} object {row.ObjectId} needs a name.");
            if (row.MaxHp < 1)
                throw new InvalidDataException($"{path} object {row.ObjectId} needs maxHp.");
            if (row.RespawnDelaySeconds < 0)
                throw new InvalidDataException($"{path} object {row.ObjectId} has negative respawnDelaySeconds.");
            if (row.RespawnRandomSeconds < 0)
                throw new InvalidDataException($"{path} object {row.ObjectId} has negative respawnRandomSeconds.");

            npcs.Add(new SpawnNpcDefinition(
                row.ObjectId,
                row.NpcId,
                row.Name.Trim(),
                row.X,
                row.Y,
                row.Z,
                row.Heading,
                row.Attackable,
                row.MaxHp,
                row.RunSpeed <= 0 ? 80f : row.RunSpeed,
                row.WalkSpeed <= 0 ? 40f : row.WalkSpeed,
                Math.Max(0, row.RespawnDelaySeconds),
                Math.Max(0, row.RespawnRandomSeconds)));
        }

        return new SpawnMapDefinition(file.MapId.Trim(), file.Enabled, npcs);
    }

    sealed class MapFileDto
    {
        public string? MapId { get; set; }
        public bool Enabled { get; set; } = true;
        public List<NpcRowDto>? Npcs { get; set; }
    }

    sealed class NpcRowDto
    {
        public int ObjectId { get; set; }
        public int NpcId { get; set; }
        public string? Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
        public bool Attackable { get; set; } = true;
        public int MaxHp { get; set; } = 96;
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public int RespawnDelaySeconds { get; set; } = 60;
        public int RespawnRandomSeconds { get; set; }
    }
}

/// <summary>Shared helper so spawn and item loaders resolve the same <c>data/</c> root.</summary>
internal static class GameDataPaths
{
    internal static string? FindDataDirectory(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            var data = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(Path.Combine(data, SpawnCatalog.SpawnsDirectoryName)))
                return data;
            if (File.Exists(Path.Combine(data, GameData.ItemsFileName)))
                return data;
            dir = dir.Parent;
        }

        return null;
    }
}
