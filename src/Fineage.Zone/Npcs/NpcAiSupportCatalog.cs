using System.Text.Json;

namespace Fineage.Zone.Npcs;

/// <summary>One Mobius AISkillScope HEAL / BUFF / DEBUFF row for NPC AI.</summary>
public readonly record struct NpcAiSupportSkill(
    int SkillId,
    int Level,
    int HitTimeMs,
    int ReuseDelayMs,
    int Mp,
    int MpInitial,
    int Amount = 0,
    int AbnormalTimeSeconds = 0,
    int AbnormalLevel = 0);

/// <summary>Per-NPC support skill lists (heal / buff / debuff) from <c>data/npc-ai-support.json</c>.</summary>
public sealed class NpcAiSupportCatalog
{
    public const string FileName = "npc-ai-support.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static readonly object CacheGate = new();
    static string? _cacheKey;
    static NpcAiSupportCatalog? _cache;

    NpcAiSupportCatalog(IReadOnlyDictionary<int, NpcAiSupportRow> byId) => ById = byId;

    public IReadOnlyDictionary<int, NpcAiSupportRow> ById { get; }

    public NpcAiSupportRow? Get(int npcId) => ById.TryGetValue(npcId, out var row) ? row : null;

    public static NpcAiSupportCatalog LoadDefault()
    {
        var directory = Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
                        ?? Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new DirectoryNotFoundException($"Could not find data/{FileName}.");
        return Load(directory);
    }

    public static NpcAiSupportCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var path = Path.GetFullPath(Path.Combine(dataDirectory, FileName));
        if (!File.Exists(path))
            throw new FileNotFoundException($"NPC AI support file not found: {path}", path);

        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        lock (CacheGate)
        {
            if (_cache != null && _cacheKey == key)
                return _cache;
            var loaded = Read(path);
            _cache = loaded;
            _cacheKey = key;
            return loaded;
        }
    }

    /// <summary>Empty catalog for tests that do not need support skills.</summary>
    public static NpcAiSupportCatalog Empty { get; } = new(new Dictionary<int, NpcAiSupportRow>());

    static NpcAiSupportCatalog Read(string path)
    {
        var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions)
                   ?? throw new InvalidDataException($"{path} was empty.");
        var byId = new Dictionary<int, NpcAiSupportRow>();
        foreach (var row in file.Npcs ?? [])
        {
            if (row.NpcId <= 0)
                continue;
            byId[row.NpcId] = new NpcAiSupportRow(
                Parse(row.Heal),
                Parse(row.Buff),
                Parse(row.Debuff));
        }
        return new NpcAiSupportCatalog(byId);
    }

    static IReadOnlyList<NpcAiSupportSkill>? Parse(List<SkillDto>? rows)
    {
        if (rows is not { Count: > 0 })
            return null;
        var list = new List<NpcAiSupportSkill>(rows.Count);
        foreach (var r in rows)
        {
            if (r.Id <= 0 || r.Level <= 0)
                continue;
            list.Add(new NpcAiSupportSkill(
                r.Id,
                r.Level,
                Math.Max(0, r.HitTimeMs),
                Math.Max(0, r.ReuseDelayMs),
                Math.Max(0, r.Mp),
                Math.Max(0, r.MpInitial),
                Math.Max(0, r.Amount),
                Math.Max(0, r.AbnormalTimeSeconds),
                Math.Max(0, r.AbnormalLevel)));
        }
        return list.Count > 0 ? list : null;
    }

    public readonly record struct NpcAiSupportRow(
        IReadOnlyList<NpcAiSupportSkill>? Heal,
        IReadOnlyList<NpcAiSupportSkill>? Buff,
        IReadOnlyList<NpcAiSupportSkill>? Debuff);

    sealed class FileDto
    {
        public List<NpcDto>? Npcs { get; set; }
    }

    sealed class NpcDto
    {
        public int NpcId { get; set; }
        public List<SkillDto>? Heal { get; set; }
        public List<SkillDto>? Buff { get; set; }
        public List<SkillDto>? Debuff { get; set; }
    }

    sealed class SkillDto
    {
        public int Id { get; set; }
        public int Level { get; set; }
        public int HitTimeMs { get; set; }
        public int ReuseDelayMs { get; set; }
        public int Mp { get; set; }
        public int MpInitial { get; set; }
        public int Amount { get; set; }
        public int AbnormalTimeSeconds { get; set; }
        public int AbnormalLevel { get; set; }
    }
}
