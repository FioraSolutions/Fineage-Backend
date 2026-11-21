using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Services;

namespace Fineage.Zone.Augmentation;

public sealed record AugmentOptionDefinition(
    int Id,
    string Name,
    IReadOnlyList<AugmentStatRow> Stats,
    IReadOnlyList<AugmentSkillRow> Skills);

public sealed record AugmentStatRow(string Kind, string Stat, double Val);

public sealed record AugmentSkillRow(int SkillId, int Level);

public sealed record AugmentChanceRow(
    string WeaponType,
    int StoneId,
    int VariationId,
    int CategoryChance,
    int AugmentId,
    double Chance);

public sealed record AugmentSkillMapRow(int Id, int SkillId, int SkillLevel, string Type);

/// <summary>Mobius augmentation options, retail chances, and skill map (data/augmentation/*.json).</summary>
public sealed class AugmentationCatalog
{
    public const string OptionsFile = "augmentation/options.json";
    public const string ChancesFile = "augmentation/retail-chances.json";
    public const string SkillMapFile = "augmentation/skillmap.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, AugmentOptionDefinition> _options;
    readonly Dictionary<(string WeaponType, int StoneId, int VariationId), List<AugmentChanceRow>> _chances;
    readonly Dictionary<int, AugmentSkillMapRow> _skillmap;

    AugmentationCatalog(
        Dictionary<int, AugmentOptionDefinition> options,
        Dictionary<(string, int, int), List<AugmentChanceRow>> chances,
        Dictionary<int, AugmentSkillMapRow> skillmap)
    {
        _options = options;
        _chances = chances;
        _skillmap = skillmap;
    }

    public static AugmentationCatalog Empty { get; } = new([], [], []);

    public int OptionCount => _options.Count;
    public bool IsLoaded => _options.Count > 0;

    public bool TryGetOption(int id, out AugmentOptionDefinition option) => _options.TryGetValue(id, out option!);

    public bool TryGetSkillMap(int augmentId, out AugmentSkillMapRow row) => _skillmap.TryGetValue(augmentId, out row!);

    public IReadOnlyList<AugmentChanceRow> Chances(string weaponType, int stoneId, int variationId)
    {
        if (_chances.TryGetValue((weaponType, stoneId, variationId), out var rows))
            return rows;
        return Array.Empty<AugmentChanceRow>();
    }

    public static AugmentationCatalog LoadDefault()
    {
        string? optionsPath = TeleportCatalog.FindFile(OptionsFile);
        string? chancesPath = TeleportCatalog.FindFile(ChancesFile);
        string? skillmapPath = TeleportCatalog.FindFile(SkillMapFile);
        if (optionsPath == null || chancesPath == null || skillmapPath == null)
        {
            ZoneLog.Warn("Augmentation catalog missing (data/augmentation/*.json). Refine disabled.");
            return Empty;
        }

        try
        {
            var options = ParseOptions(File.ReadAllText(optionsPath), optionsPath);
            var chances = ParseChances(File.ReadAllText(chancesPath), chancesPath);
            var skillmap = ParseSkillMap(File.ReadAllText(skillmapPath), skillmapPath);
            return new AugmentationCatalog(options, chances, skillmap);
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"Augmentation catalog failed to load: {ex.Message}");
            return Empty;
        }
    }

    public static AugmentationCatalog ParseOptionsChancesSkillMap(string optionsJson, string chancesJson, string skillmapJson)
    {
        return new AugmentationCatalog(
            ParseOptions(optionsJson, OptionsFile),
            ParseChances(chancesJson, ChancesFile),
            ParseSkillMap(skillmapJson, SkillMapFile));
    }

    static Dictionary<int, AugmentOptionDefinition> ParseOptions(string json, string source)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("options", out var opts) || opts.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{source}: missing options object.");

        var byId = new Dictionary<int, AugmentOptionDefinition>();
        foreach (var prop in opts.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, out int id))
                continue;
            var row = prop.Value;
            var stats = new List<AugmentStatRow>();
            if (row.TryGetProperty("stats", out var statsArr) && statsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in statsArr.EnumerateArray())
                {
                    string kind = s.TryGetProperty("kind", out var k) ? k.GetString() ?? "add" : "add";
                    string stat = s.TryGetProperty("stat", out var st) ? st.GetString() ?? "" : "";
                    double val = s.TryGetProperty("val", out var v) && v.TryGetDouble(out double d) ? d : 0;
                    if (!string.IsNullOrEmpty(stat))
                        stats.Add(new AugmentStatRow(kind, stat, val));
                }
            }

            var skills = new List<AugmentSkillRow>();
            if (row.TryGetProperty("skills", out var skArr) && skArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in skArr.EnumerateArray())
                {
                    int sid = s.TryGetProperty("skillId", out var sidEl) ? sidEl.GetInt32() : 0;
                    int lvl = s.TryGetProperty("level", out var lvlEl) ? lvlEl.GetInt32() : 1;
                    if (sid > 0)
                        skills.Add(new AugmentSkillRow(sid, lvl));
                }
            }

            string name = row.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            byId[id] = new AugmentOptionDefinition(id, name, stats, skills);
        }

        return byId;
    }

    static Dictionary<(string, int, int), List<AugmentChanceRow>> ParseChances(string json, string source)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("chances", out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{source}: missing chances array.");

        var map = new Dictionary<(string, int, int), List<AugmentChanceRow>>();
        foreach (var row in arr.EnumerateArray())
        {
            string wt = row.GetProperty("weaponType").GetString() ?? "";
            int stone = row.GetProperty("stoneId").GetInt32();
            int varId = row.GetProperty("variationId").GetInt32();
            int cat = row.GetProperty("categoryChance").GetInt32();
            int augId = row.GetProperty("augmentId").GetInt32();
            double ch = row.GetProperty("chance").GetDouble();
            var key = (wt, stone, varId);
            if (!map.TryGetValue(key, out var list))
            {
                list = [];
                map[key] = list;
            }

            list.Add(new AugmentChanceRow(wt, stone, varId, cat, augId, ch));
        }

        return map;
    }

    static Dictionary<int, AugmentSkillMapRow> ParseSkillMap(string json, string source)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("skillmap", out var sm) || sm.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{source}: missing skillmap.");

        var map = new Dictionary<int, AugmentSkillMapRow>();
        foreach (var prop in sm.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, out int id))
                continue;
            var row = prop.Value;
            int skillId = row.TryGetProperty("skillId", out var s) ? s.GetInt32() : 0;
            int skillLevel = row.TryGetProperty("skillLevel", out var l) ? l.GetInt32() : 0;
            string type = row.TryGetProperty("type", out var t) ? t.GetString() ?? "blue" : "blue";
            map[id] = new AugmentSkillMapRow(id, skillId, skillLevel, type);
        }

        return map;
    }
}