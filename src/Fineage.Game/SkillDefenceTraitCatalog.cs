using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game;

/// <summary>
/// Mobius <c>DefenceTrait</c> percent tables from <c>skill-passives.json</c> (jewelry resists etc.).
/// Values are Mobius raw percents (30 = +30% resist); convert with <c>(100 + raw) / 100</c> when folding.
/// </summary>
public sealed class SkillDefenceTraitCatalog
{
    public const string FileName = SkillStatEffectCatalog.PassivesFileName;

    readonly Dictionary<int, IReadOnlyDictionary<string, double[]>> _bySkill;

    SkillDefenceTraitCatalog(Dictionary<int, IReadOnlyDictionary<string, double[]>> bySkill) =>
        _bySkill = bySkill;

    public static readonly SkillDefenceTraitCatalog Empty = new([]);

    public int Count => _bySkill.Count;

    public bool TryGet(int skillId, out IReadOnlyDictionary<string, double[]> traits) =>
        _bySkill.TryGetValue(skillId, out traits!);

    /// <summary>Fold known skill levels into a trait → multiplier map (default 1).</summary>
    public IReadOnlyDictionary<string, double>? Collect(IReadOnlyDictionary<int, int> known)
    {
        Dictionary<string, double>? traits = null;
        foreach (var (skillId, level) in known)
        {
            if (!_bySkill.TryGetValue(skillId, out var row))
                continue;
            foreach (var (key, values) in row)
            {
                if (values.Length == 0)
                    continue;
                double raw = values[Math.Clamp(level, 1, values.Length) - 1];
                double mul = (100d + raw) / 100d;
                traits ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                traits.TryGetValue(key, out var cur);
                if (cur <= 0)
                    cur = 1d;
                traits[key] = cur * mul;
            }
        }

        return traits is { Count: > 0 } ? traits : null;
    }

    public static SkillDefenceTraitCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static SkillDefenceTraitCatalog Parse(string json, string source = FileName)
    {
        using var doc = JsonDocument.Parse(json);
        var bySkill = new Dictionary<int, IReadOnlyDictionary<string, double[]>>();
        if (!doc.RootElement.TryGetProperty("skills", out var skills))
            throw new InvalidDataException($"{source} has no skills.");
        foreach (var row in skills.EnumerateArray())
        {
            if (!row.TryGetProperty("defenceTraits", out var traitsEl) || traitsEl.ValueKind != JsonValueKind.Object)
                continue;
            int id = row.GetProperty("skillId").GetInt32();
            var map = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in traitsEl.EnumerateObject())
            {
                var values = prop.Value.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                if (values.Length > 0)
                    map[prop.Name.ToLowerInvariant()] = values;
            }

            if (map.Count > 0)
                bySkill[id] = map;
        }

        return new SkillDefenceTraitCatalog(bySkill);
    }
}
