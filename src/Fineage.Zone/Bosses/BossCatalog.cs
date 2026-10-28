using System.Text.Json;
using Fineage.Game;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.Bosses;

/// <summary>
/// Loads <c>data/raidbosses.json</c>. Stats come from <see cref="NpcCatalog"/>.
/// Object ids occupy [80001, <see cref="PlayerInventory.FirstObjectId"/>).
/// Rows outside Talking Island are stored with <see cref="BossDefinition.Enabled"/> false,
/// the same way town spawn maps stay disabled.
/// </summary>
public sealed class BossCatalog
{
    public const string FileName = "raidbosses.json";
    public const int FirstObjectId = 80_001;
    public const long MillisPerSecond = 1000;
    public const long MillisPerHour = 3_600_000;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    BossCatalog(IReadOnlyDictionary<int, BossDefinition> byId, IReadOnlyList<BossDefinition> bosses)
    {
        ById = byId;
        Bosses = bosses;
    }

    public IReadOnlyDictionary<int, BossDefinition> ById { get; }

    public IReadOnlyList<BossDefinition> Bosses { get; }

    public bool TryGet(int npcId, out BossDefinition definition) => ById.TryGetValue(npcId, out definition!);

    public BossDefinition Get(int npcId)
    {
        if (!ById.TryGetValue(npcId, out var definition))
            throw new KeyNotFoundException($"Boss {npcId} is not in the catalog.");
        return definition;
    }

    public static BossCatalog From(IReadOnlyList<BossDefinition> bosses)
    {
        ArgumentNullException.ThrowIfNull(bosses);
        if (bosses.Count == 0)
            throw new ArgumentException("Boss catalog needs at least one row.", nameof(bosses));

        var byId = new Dictionary<int, BossDefinition>(bosses.Count);
        var ordered = new List<BossDefinition>(bosses.Count);
        foreach (var boss in bosses.OrderBy(boss => boss.NpcId))
        {
            if (!byId.TryAdd(boss.NpcId, boss))
                throw new InvalidDataException($"Duplicate boss npc id {boss.NpcId}.");
            ordered.Add(boss);
        }

        return new BossCatalog(byId, ordered);
    }

    public static BossCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Boss catalog not found: {path}", path);

        var templates = NpcCatalog.Load(dataDirectory);
        return Read(path, templates);
    }

    public static BossCatalog LoadDefault()
    {
        var directory = Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new DirectoryNotFoundException("Could not find data/raidbosses.json. Pass the data directory to BossCatalog.Load.");
        return Load(directory);
    }

    /// <summary>
    /// Mobius <c>Spawn.setRespawnDelay</c>: seconds become milliseconds,
    /// and the random interval widens the window on both sides. A zero delay does not respawn.
    /// </summary>
    public static (long MinMillis, long MaxMillis) RaidWindow(int delaySeconds, int randomSeconds)
    {
        if (delaySeconds == 0)
            return (0, 0);

        long minSeconds = Math.Max(1, (long)delaySeconds - randomSeconds);
        long maxSeconds = Math.Max(1, (long)delaySeconds + randomSeconds);
        return (minSeconds * MillisPerSecond, maxSeconds * MillisPerSecond);
    }

    /// <summary>
    /// Mobius grand-boss scripts: <c>intervalHours * hour + random(-randomHours, +randomHours)</c>, in milliseconds.
    /// </summary>
    public static (long MinMillis, long MaxMillis) GrandWindow(int intervalHours, int randomHours)
    {
        long interval = intervalHours * MillisPerHour;
        long range = randomHours * MillisPerHour;
        return (interval - range, interval + range);
    }

    static BossCatalog Read(string path, NpcCatalog templates)
    {
        FileDto file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"{path} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }

        if (file.Bosses == null || file.Bosses.Count == 0)
            throw new InvalidDataException($"{path} has no bosses.");

        var pending = new List<BossRowDto>(file.Bosses.Count);
        var seen = new HashSet<int>();
        foreach (var row in file.Bosses)
        {
            if (row.NpcId <= 0)
                throw new InvalidDataException($"{path} has a non-positive npcId.");
            if (!seen.Add(row.NpcId))
                throw new InvalidDataException($"{path} lists boss {row.NpcId} twice.");
            pending.Add(row);
        }

        pending.Sort((a, b) => a.NpcId.CompareTo(b.NpcId));
        if (FirstObjectId + pending.Count > PlayerInventory.FirstObjectId)
            throw new InvalidDataException($"{path} does not fit in the boss object id band.");

        var bosses = new List<BossDefinition>(pending.Count);
        for (int i = 0; i < pending.Count; i++)
        {
            var row = pending[i];
            if (!templates.ById.TryGetValue(row.NpcId, out var template))
                throw new InvalidDataException($"{path} boss {row.NpcId} has no npc template.");

            var kind = ParseKind(path, row);
            var (min, max) = Window(path, row, kind);
            var initial = kind == BossKind.Grand
                ? ParseStatus(path, row.NpcId, row.InitialStatus)
                : BossLifeStatus.Alive;

            bosses.Add(new BossDefinition(
                row.NpcId,
                kind,
                row.Enabled,
                FirstObjectId + i,
                string.IsNullOrWhiteSpace(template.Name) ? $"Boss {row.NpcId}" : template.Name,
                template.Title,
                template.Type,
                row.X,
                row.Y,
                row.Z,
                row.Heading,
                template.Attackable,
                template.Hp,
                template.Mp,
                template.RunSpeed,
                template.WalkSpeed,
                min,
                max,
                initial));
        }

        return From(bosses);
    }

    static (long Min, long Max) Window(string path, BossRowDto row, BossKind kind)
    {
        if (kind == BossKind.Raid)
        {
            if (row.RespawnDelaySeconds < 0 || row.RespawnRandomSeconds < 0)
                throw new InvalidDataException($"{path} boss {row.NpcId} has a negative raid respawn window.");
            return RaidWindow(row.RespawnDelaySeconds, row.RespawnRandomSeconds);
        }

        if (row.RespawnIntervalHours < 1 || row.RespawnRandomHours < 0)
            throw new InvalidDataException($"{path} boss {row.NpcId} needs a grand-boss hour window.");
        return GrandWindow(row.RespawnIntervalHours, row.RespawnRandomHours);
    }

    static BossKind ParseKind(string path, BossRowDto row)
    {
        if (string.Equals(row.Kind, "raid", StringComparison.OrdinalIgnoreCase))
            return BossKind.Raid;
        if (string.Equals(row.Kind, "grand", StringComparison.OrdinalIgnoreCase))
            return BossKind.Grand;
        throw new InvalidDataException($"{path} boss {row.NpcId} needs kind raid or grand.");
    }

    static BossLifeStatus ParseStatus(string path, int npcId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "alive", StringComparison.OrdinalIgnoreCase))
            return BossLifeStatus.Alive;
        if (string.Equals(text, "waiting", StringComparison.OrdinalIgnoreCase))
            return BossLifeStatus.Waiting;
        if (string.Equals(text, "fighting", StringComparison.OrdinalIgnoreCase))
            return BossLifeStatus.Fighting;
        if (string.Equals(text, "dead", StringComparison.OrdinalIgnoreCase))
            return BossLifeStatus.Dead;
        throw new InvalidDataException($"{path} boss {npcId} has initial status '{text}'.");
    }

    sealed class FileDto
    {
        public List<BossRowDto>? Bosses { get; set; }
    }

    sealed class BossRowDto
    {
        public int NpcId { get; set; }
        public string? Kind { get; set; }
        public bool Enabled { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
        public int RespawnDelaySeconds { get; set; }
        public int RespawnRandomSeconds { get; set; }
        public int RespawnIntervalHours { get; set; }
        public int RespawnRandomHours { get; set; }
        public string? InitialStatus { get; set; }
    }
}
