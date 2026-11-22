using System.Text.Json;

namespace Fineage.Game.Skills;

/// <summary>Checks <c>data/**/*.json</c> for skill id fields that are not in the Interlude whitelist.</summary>
public static class InterludeSkillReferenceValidator
{
    static readonly string[] SkillIdPropertyNames =
    [
        "skillId",
        "skill_id",
        "learnSkillId",
        "enchantSkillId",
        "triggerSkillId",
        "chanceSkillId",
        "supportSkillId",
        "cubicSkillId",
    ];

    static readonly string[] NestedSkillObjectNames = ["skill", "cubic", "summonSkill"];

    public static IReadOnlyList<string> ViolationsUnderDataDirectory(string dataDirectory, InterludeSkillWhitelist whitelist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(whitelist);

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dataDirectory, "*.json", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(dataDirectory, file).Replace('\\', '/');
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                Walk(doc.RootElement, relative, violations, whitelist, inSkillContext: false);
            }
            catch (JsonException ex)
            {
                violations.Add($"data/{relative}: invalid JSON ({ex.Message})");
            }
        }

        violations.Sort(StringComparer.Ordinal);
        return violations;
    }

    static void Walk(
        JsonElement element,
        string source,
        List<string> violations,
        InterludeSkillWhitelist whitelist,
        bool inSkillContext)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    bool childSkillContext = inSkillContext;
                    foreach (var prop in element.EnumerateObject())
                    {
                        if (IsSkillContextProperty(prop.Name))
                            childSkillContext = true;

                        if (IsSkillIdProperty(prop.Name) &&
                            prop.Value.ValueKind == JsonValueKind.Number &&
                            prop.Value.TryGetInt32(out int skillId) &&
                            skillId > 0 &&
                            !whitelist.Contains(skillId))
                        {
                            violations.Add($"non-Interlude skillId={skillId} source=data/{source}:{prop.Name}");
                        }

                        if (NestedSkillObjectNames.Contains(prop.Name, StringComparer.Ordinal) &&
                            prop.Value.ValueKind == JsonValueKind.Object &&
                            prop.Value.TryGetProperty("id", out var nestedId) &&
                            nestedId.ValueKind == JsonValueKind.Number &&
                            nestedId.TryGetInt32(out int nestedSkillId) &&
                            nestedSkillId > 0 &&
                            nestedSkillId < 20_000 &&
                            !whitelist.Contains(nestedSkillId))
                        {
                            violations.Add($"non-Interlude skillId={nestedSkillId} source=data/{source}:{prop.Name}.id");
                        }

                        Walk(prop.Value, source, violations, whitelist, childSkillContext);
                    }

                    break;
                }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Walk(item, source, violations, whitelist, inSkillContext);
                break;
        }
    }

    static bool IsSkillIdProperty(string name) =>
        SkillIdPropertyNames.Contains(name, StringComparer.Ordinal);

    static bool IsSkillContextProperty(string name) =>
        name.Contains("skill", StringComparison.OrdinalIgnoreCase) ||
        name is "heal" or "buff" or "debuff" or "immobilize" or "silence" or "suicide" or "grants" or "effects" or "setSkills";
}