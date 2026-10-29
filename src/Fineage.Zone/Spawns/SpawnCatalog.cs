using System.Text.Json;
using Fineage.Game;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.Spawns;

/// <summary>
/// Loads Interlude-style fixed spawn rows from <c>data/spawns/*.json</c> (Mobius spawnlist subset).
/// Rows that omit name, attackable, hp, or speeds take those from <see cref="NpcCatalog"/>.
/// Inline values, such as the Cedric gremlin rows, win.
/// </summary>
public sealed class SpawnCatalog
{
    public const string DefaultMapId = "ti_cedric";
    public const string TalkingIslandVillageMapId = "ti_village";
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

        var templates = NpcCatalog.Load(dataDirectory);
        var pending = new List<PendingMap>();
        var mapIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(spawnsDir, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var map = ReadPending(path);
            if (!mapIds.Add(map.MapId))
                throw new InvalidDataException($"Duplicate spawn map id '{map.MapId}' in {path}.");
            pending.Add(map);
        }

        if (pending.Count == 0)
            throw new InvalidDataException($"No spawn maps under {spawnsDir}.");

        var ids = new NpcObjectIdAllocator();
        foreach (var map in pending)
        {
            foreach (var row in map.Npcs)
            {
                if (row.ObjectId is not int objectId)
                    continue;
                try
                {
                    ids.Reserve(objectId);
                }
                catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
                {
                    throw new InvalidDataException(
                        $"{map.Path} object {objectId} is outside the NPC id range or duplicated.",
                        ex);
                }
            }
        }

        var maps = new Dictionary<string, SpawnMapDefinition>(StringComparer.Ordinal);
        foreach (var map in pending)
        {
            var npcs = new List<SpawnNpcDefinition>(map.Npcs.Count);
            foreach (var row in map.Npcs)
            {
                if (!templates.ById.TryGetValue(row.NpcId, out var template))
                    throw new InvalidDataException($"{map.Path} object references missing npc template {row.NpcId}.");

                int objectId = row.ObjectId ?? Allocate(ids, map.Path);
                string name = string.IsNullOrWhiteSpace(row.Name) ? template.Name : row.Name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException($"{map.Path} npc {row.NpcId} needs a name.");

                int maxHp = row.MaxHp ?? template.Hp;
                if (maxHp < 1)
                    throw new InvalidDataException($"{map.Path} object {objectId} needs maxHp.");
                if (row.RespawnDelaySeconds < 0)
                    throw new InvalidDataException($"{map.Path} object {objectId} has negative respawnDelaySeconds.");
                if (row.RespawnRandomSeconds < 0)
                    throw new InvalidDataException($"{map.Path} object {objectId} has negative respawnRandomSeconds.");

                npcs.Add(new SpawnNpcDefinition(
                    objectId,
                    row.NpcId,
                    name,
                    row.X,
                    row.Y,
                    row.Z,
                    row.Heading,
                    row.Attackable ?? template.Attackable,
                    maxHp,
                    Speed(row.RunSpeed, template.RunSpeed, 80f),
                    Speed(row.WalkSpeed, template.WalkSpeed, 40f),
                    Math.Max(0, row.RespawnDelaySeconds),
                    Math.Max(0, row.RespawnRandomSeconds),
                    template.Title,
                    template.Type,
                    Math.Max(1, template.Level),
                    template.Exp,
                    template.Sp));
            }

            maps.Add(map.MapId, new SpawnMapDefinition(map.MapId, map.Enabled, npcs));
        }

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

    static int Allocate(NpcObjectIdAllocator ids, string path)
    {
        try
        {
            return ids.Next();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException($"{path} could not allocate an NPC object id.", ex);
        }
    }

    static float Speed(float? row, float template, float fallbackWhenNonPositive)
    {
        if (row == null)
            return template > 0 ? template : fallbackWhenNonPositive;
        return row.Value > 0 ? row.Value : fallbackWhenNonPositive;
    }

    static PendingMap ReadPending(string path)
    {
        MapFileDto file;
        try
        {
            file = JsonSerializer.Deserialize<MapFileDto>(File.ReadAllText(path), JsonOptions)
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

        var npcs = new List<PendingNpc>(file.Npcs.Count);
        foreach (var row in file.Npcs)
        {
            if (row.NpcId <= 0)
                throw new InvalidDataException($"{path} has a row with no npcId.");
            if (row.ObjectId is int explicitId && explicitId <= 0)
                throw new InvalidDataException($"{path} has a non-positive objectId.");
            npcs.Add(new PendingNpc(
                row.ObjectId,
                row.NpcId,
                row.Name,
                row.X,
                row.Y,
                row.Z,
                row.Heading,
                row.Attackable,
                row.MaxHp,
                row.RunSpeed,
                row.WalkSpeed,
                row.RespawnDelaySeconds,
                row.RespawnRandomSeconds));
        }

        return new PendingMap(path, file.MapId.Trim(), file.Enabled, npcs);
    }

    sealed record PendingMap(string Path, string MapId, bool Enabled, List<PendingNpc> Npcs);

    sealed record PendingNpc(
        int? ObjectId,
        int NpcId,
        string? Name,
        int X,
        int Y,
        int Z,
        int Heading,
        bool? Attackable,
        int? MaxHp,
        float? RunSpeed,
        float? WalkSpeed,
        int RespawnDelaySeconds,
        int RespawnRandomSeconds);

    sealed class MapFileDto
    {
        public string? MapId { get; set; }
        public bool Enabled { get; set; } = true;
        public List<NpcRowDto>? Npcs { get; set; }
    }

    sealed class NpcRowDto
    {
        public int? ObjectId { get; set; }
        public int NpcId { get; set; }
        public string? Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
        public bool? Attackable { get; set; }
        public int? MaxHp { get; set; }
        public float? RunSpeed { get; set; }
        public float? WalkSpeed { get; set; }
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
