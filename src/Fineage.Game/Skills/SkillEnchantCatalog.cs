using System.Text.Json;

namespace Fineage.Game.Skills;

/// <summary>Mobius <c>EnchantSkillTreeData.xml</c> trainer +skill routes (SP, EXP, success rates).</summary>
public sealed class SkillEnchantCatalog
{
    public const string FileName = "skill-enchant-tree.json";
    public const int MinPlayerLevel = 76;
    public const int MinClassId = 88;
    public const int SpellbookItemId = 6622;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly List<SkillEnchantRoute> _routes;
    readonly Dictionary<(int SkillId, int Level), SkillEnchantRoute> _byKey;

    SkillEnchantCatalog(List<SkillEnchantRoute> routes)
    {
        _routes = routes;
        _byKey = new Dictionary<(int, int), SkillEnchantRoute>(routes.Count);
        foreach (var route in routes)
            _byKey[(route.SkillId, route.Level)] = route;
    }

    public IReadOnlyList<SkillEnchantRoute> Routes => _routes;

    public static SkillEnchantCatalog Empty { get; } = new([]);

    public bool IsLoaded => _routes.Count > 0;

    public SkillEnchantRoute? Find(int skillId, int level) =>
        _byKey.TryGetValue((skillId, level), out var row) ? row : null;

    /// <summary>Mobius <c>getAvailableEnchantSkills</c>: next enchant when the known level equals <c>minSkillLevel</c>.</summary>
    public IReadOnlyList<SkillEnchantRoute> Available(
        IReadOnlyDictionary<int, int> knownSkills,
        int playerLevel)
    {
        if (playerLevel < MinPlayerLevel || _routes.Count == 0)
            return Array.Empty<SkillEnchantRoute>();
        var result = new List<SkillEnchantRoute>();
        foreach (var route in _routes)
        {
            if (!knownSkills.TryGetValue(route.SkillId, out int have) || have != route.MinSkillLevel)
                continue;
            result.Add(route);
        }

        return result;
    }

    public static SkillEnchantCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Skill enchant datapack not found: {path}", path);
        try
        {
            return Parse(File.ReadAllText(path), path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }
    }

    public static SkillEnchantCatalog Parse(string json, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        string where = source ?? FileName;
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"{where} was empty.");
        var routes = new List<SkillEnchantRoute>();
        var seen = new HashSet<(int, int)>();
        foreach (var row in file.Routes ?? [])
        {
            if (row.SkillId <= 0 || row.Level <= 0)
                throw new InvalidDataException($"{where} has an invalid skill row.");
            if (!seen.Add((row.SkillId, row.Level)))
                throw new InvalidDataException($"{where} repeats skill {row.SkillId} level {row.Level}.");
            routes.Add(new SkillEnchantRoute(
                row.SkillId,
                row.Level,
                row.Name ?? "",
                row.Desc ?? "",
                row.BaseLevel,
                row.MinSkillLevel,
                Math.Max(0, row.Sp),
                Math.Max(0, row.Exp),
                row.Rate76,
                row.Rate77,
                row.Rate78,
                row.Rate79,
                row.Rate80));
        }

        return new SkillEnchantCatalog(routes);
    }

    sealed class FileDto
    {
        public List<RouteDto>? Routes { get; set; }
    }

    sealed class RouteDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public string? Name { get; set; }
        public string? Desc { get; set; }
        public int BaseLevel { get; set; }
        public int MinSkillLevel { get; set; }
        public int Sp { get; set; }
        public int Exp { get; set; }
        public int Rate76 { get; set; }
        public int Rate77 { get; set; }
        public int Rate78 { get; set; }
        public int Rate79 { get; set; }
        public int Rate80 { get; set; }
    }
}

public sealed record SkillEnchantRoute(
    int SkillId,
    int Level,
    string Name,
    string Desc,
    int BaseLevel,
    int MinSkillLevel,
    int Sp,
    long Exp,
    int Rate76,
    int Rate77,
    int Rate78,
    int Rate79,
    int Rate80)
{
    public int RateForPlayerLevel(int level) => level switch
    {
        <= 76 => Rate76,
        77 => Rate77,
        78 => Rate78,
        79 => Rate79,
        _ => Rate80
    };
}