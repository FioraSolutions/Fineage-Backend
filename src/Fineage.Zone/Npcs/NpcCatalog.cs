using System.Text.Json;

namespace Fineage.Zone.Npcs;

/// <summary>
/// One Interlude NPC template transcribed from L2J Mobius <c>stats/npcs</c>.
/// <see cref="Type"/> is the Mobius instance (Merchant, Guard, Teleporter, Folk, …).
/// <see cref="Attackable"/> follows Mobius: missing <c>status attackable</c> means true.
/// <see cref="Aggressive"/> follows Mobius: missing <c>ai isAggressive</c> means true.
/// </summary>
public sealed record NpcTemplate(
    int NpcId,
    string Name,
    string Title,
    string Type,
    int Level,
    float CollisionRadius,
    float CollisionHeight,
    bool Attackable,
    bool Aggressive,
    int Hp,
    int Mp,
    float RunSpeed,
    float WalkSpeed,
    long Exp = 0,
    int Sp = 0,
    double PAtk = 0,
    double PDef = 0,
    int AttackSpeed = 0,
    int AttackRange = 0,
    double Accuracy = 0,
    double Evasion = 0,
    int Critical = 0,
    int RandomDamage = 0,
    int Dex = 30);

/// <summary>Loads <c>data/npcs.json</c>.</summary>
public sealed class NpcCatalog
{
    public const string FileName = "npcs.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    static readonly object CacheGate = new();
    static string? _cacheKey;
    static NpcCatalog? _cache;

    NpcCatalog(IReadOnlyDictionary<int, NpcTemplate> byId)
    {
        ById = byId;
    }

    public IReadOnlyDictionary<int, NpcTemplate> ById { get; }

    public NpcTemplate Get(int npcId)
    {
        if (!ById.TryGetValue(npcId, out var template))
            throw new KeyNotFoundException($"NPC template {npcId} is not in the catalog.");
        return template;
    }

    public static NpcCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var path = Path.GetFullPath(Path.Combine(dataDirectory, FileName));
        if (!File.Exists(path))
            throw new FileNotFoundException($"NPC template file not found: {path}", path);

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

    public static NpcCatalog LoadDefault()
    {
        var directory = Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            throw new DirectoryNotFoundException("Could not find data/npcs.json. Pass the data directory to NpcCatalog.Load.");
        return Load(directory);
    }

    static NpcCatalog Read(string path)
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

        if (file.Npcs == null || file.Npcs.Count == 0)
            throw new InvalidDataException($"{path} has no npcs.");

        var byId = new Dictionary<int, NpcTemplate>(file.Npcs.Count);
        foreach (var row in file.Npcs)
        {
            if (row.NpcId <= 0)
                throw new InvalidDataException($"{path} has a non-positive npcId.");
            if (byId.ContainsKey(row.NpcId))
                throw new InvalidDataException($"{path} lists npc {row.NpcId} twice.");
            if (string.IsNullOrWhiteSpace(row.Type))
                throw new InvalidDataException($"{path} npc {row.NpcId} needs a type.");
            if (row.Level < 0)
                throw new InvalidDataException($"{path} npc {row.NpcId} has a negative level.");
            if (row.Hp < 1 || row.Mp < 1)
                throw new InvalidDataException($"{path} npc {row.NpcId} needs hp and mp.");
            if (row.RunSpeed <= 0 || row.WalkSpeed <= 0)
                throw new InvalidDataException($"{path} npc {row.NpcId} needs run and walk speed.");
            if (row.CollisionRadius <= 0 || row.CollisionHeight <= 0)
                throw new InvalidDataException($"{path} npc {row.NpcId} needs collision radius and height.");

            byId.Add(row.NpcId, new NpcTemplate(
                row.NpcId,
                (row.Name ?? "").Trim(),
                (row.Title ?? "").Trim(),
                row.Type.Trim(),
                row.Level,
                row.CollisionRadius,
                row.CollisionHeight,
                row.Attackable,
                row.Aggressive,
                row.Hp,
                row.Mp,
                row.RunSpeed,
                row.WalkSpeed,
                Math.Max(0, row.Exp),
                Math.Max(0, row.Sp),
                Math.Max(0, row.PAtk),
                Math.Max(0, row.PDef),
                Math.Max(0, row.AttackSpeed),
                Math.Max(0, row.AttackRange),
                row.Accuracy,
                row.Evasion,
                Math.Max(0, row.Critical),
                Math.Max(0, row.RandomDamage),
                row.Dex > 0 ? row.Dex : 30));
        }

        return new NpcCatalog(byId);
    }

    sealed class FileDto
    {
        public List<NpcDto>? Npcs { get; set; }
    }

    sealed class NpcDto
    {
        public int NpcId { get; set; }
        public string? Name { get; set; }
        public string? Title { get; set; }
        public string? Type { get; set; }
        public int Level { get; set; }
        public float CollisionRadius { get; set; }
        public float CollisionHeight { get; set; }
        public bool Attackable { get; set; } = true;
        public bool Aggressive { get; set; } = true;
        public int Hp { get; set; }
        public int Mp { get; set; }
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public long Exp { get; set; }
        public int Sp { get; set; }
        /// <summary>Mobius <c>stats/attack physical</c> (level-scaled base P.Atk).</summary>
        public double PAtk { get; set; }
        /// <summary>Mobius <c>stats/defence physical</c>.</summary>
        public double PDef { get; set; }
        /// <summary>Mobius <c>stats/attack attackSpeed</c> (Pspd).</summary>
        public int AttackSpeed { get; set; }
        /// <summary>Mobius <c>stats/attack range</c>.</summary>
        public int AttackRange { get; set; }
        /// <summary>Mobius <c>attack accuracy</c> base (sqrt(DEX)*6 + level added at runtime).</summary>
        public double Accuracy { get; set; }
        /// <summary>Mobius <c>defence evasion</c> base.</summary>
        public double Evasion { get; set; }
        /// <summary>Mobius <c>attack critical</c> base rate (×10 × DEX bonus).</summary>
        public int Critical { get; set; }
        /// <summary>Mobius <c>attack random</c> damage spread in percent.</summary>
        public int RandomDamage { get; set; }
        public int Dex { get; set; }
    }
}
