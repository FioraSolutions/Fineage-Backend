using System.Text.Json;

namespace Fineage.Zone.Drops;

/// <summary>
/// Loads hand-written Mobius-style droplist rows from <c>data/drops/*.json</c>.
/// </summary>
public sealed class DropCatalog
{
    public const string DropsDirectoryName = "drops";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    DropCatalog(IReadOnlyDictionary<int, DropNpcDefinition> byNpcId)
    {
        ByNpcId = byNpcId;
    }

    public IReadOnlyDictionary<int, DropNpcDefinition> ByNpcId { get; }

    public bool TryGet(int npcId, out DropNpcDefinition definition) => ByNpcId.TryGetValue(npcId, out definition!);

    public static DropCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var dropsDir = Path.Combine(dataDirectory, DropsDirectoryName);
        if (!Directory.Exists(dropsDir))
            throw new DirectoryNotFoundException($"Drop data directory not found: {dropsDir}");

        var byNpcId = new Dictionary<int, DropNpcDefinition>();
        foreach (string path in Directory.EnumerateFiles(dropsDir, "*.json"))
        {
            var definition = ReadFile(path);
            if (byNpcId.ContainsKey(definition.NpcId))
                throw new InvalidDataException($"Duplicate droplist npcId {definition.NpcId} in {path}.");
            byNpcId.Add(definition.NpcId, definition);
        }

        if (byNpcId.Count == 0)
            throw new InvalidDataException($"No droplist files under {dropsDir}.");

        return new DropCatalog(byNpcId);
    }

    public static DropCatalog LoadDefault()
    {
        var directory = FindDataDirectory(AppContext.BaseDirectory)
            ?? FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
        {
            throw new DirectoryNotFoundException(
                "Could not find data/drops. Pass the data directory to DropCatalog.Load.");
        }

        return Load(directory);
    }

    static string? FindDataDirectory(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            var data = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(Path.Combine(data, DropsDirectoryName)))
                return data;
            dir = dir.Parent;
        }

        return null;
    }

    static DropNpcDefinition ReadFile(string path)
    {
        DropFileDto file;
        try
        {
            var json = File.ReadAllText(path);
            file = JsonSerializer.Deserialize<DropFileDto>(json, JsonOptions)
                ?? throw new InvalidDataException($"Empty droplist JSON: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid droplist JSON in {path}.", ex);
        }

        if (file.NpcId <= 0)
            throw new InvalidDataException($"npcId must be positive in {path}.");
        if (file.Death?.Groups == null || file.Death.Groups.Count == 0)
            throw new InvalidDataException($"death.groups required in {path}.");

        var groups = new List<DropGroupDefinition>();
        foreach (var group in file.Death.Groups)
        {
            if (group.Items == null || group.Items.Count == 0)
                throw new InvalidDataException($"death group items required in {path}.");
            groups.Add(new DropGroupDefinition
            {
                Chance = group.Chance,
                Items = MapItems(group.Items, path)
            });
        }

        DropCorpseDefinition? corpse = null;
        if (file.Corpse?.Items is { Count: > 0 })
            corpse = new DropCorpseDefinition { Items = MapItems(file.Corpse.Items, path) };

        return new DropNpcDefinition
        {
            NpcId = file.NpcId,
            Death = new DropDeathDefinition { Groups = groups },
            Corpse = corpse
        };
    }

    static IReadOnlyList<DropItemDefinition> MapItems(IReadOnlyList<DropItemDto> items, string path)
    {
        var mapped = new List<DropItemDefinition>(items.Count);
        foreach (var item in items)
        {
            if (item.ItemId <= 0)
                throw new InvalidDataException($"itemId must be positive in {path}.");
            if (item.Min <= 0 || item.Max <= 0)
                throw new InvalidDataException($"min/max must be positive in {path}.");
            mapped.Add(new DropItemDefinition
            {
                ItemId = item.ItemId,
                Min = item.Min,
                Max = item.Max,
                Chance = item.Chance
            });
        }

        return mapped;
    }

    sealed class DropFileDto
    {
        public int NpcId { get; set; }
        public DropDeathDto? Death { get; set; }
        public DropCorpseDto? Corpse { get; set; }
    }

    sealed class DropDeathDto
    {
        public List<DropGroupDto>? Groups { get; set; }
    }

    sealed class DropGroupDto
    {
        public double Chance { get; set; } = 100;
        public List<DropItemDto>? Items { get; set; }
    }

    sealed class DropCorpseDto
    {
        public List<DropItemDto>? Items { get; set; }
    }

    sealed class DropItemDto
    {
        public int ItemId { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public double Chance { get; set; } = 100;
    }
}
