using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// data/skills.json reuseDelayMs (template and per-level overrides) must match the Mobius CT_0
/// Interlude reuseDelay snapshot. The snapshot is generated from the XML table values (including
/// #reuseDelay), not copied as source. Live MReuse/PReuse scaling still happens in ZoneCombat.SkillReuseMs.
/// </summary>
public sealed class SkillReuseParityTests
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Skills_json_reuse_matches_generated_mobius_snapshot()
    {
        string snapPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "skill-reuse-snapshot.json");
        Assert.True(File.Exists(snapPath), snapPath);
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(snapPath), JsonOptions)
            ?? throw new InvalidDataException("empty snapshot");

        var catalog = GameData.LoadDefault().Skills;
        var npcAi = SkillParityTestHelpers.NpcAiSupportSkillIds();

        var mismatches = new List<string>();
        foreach (var skill in catalog.Skills)
        {
            if (!snapshot.TryGetValue(skill.SkillId.ToString(), out var expected))
            {
                if (npcAi.Contains(skill.SkillId))
                    continue;
                mismatches.Add($"skill {skill.SkillId} ({skill.Name}) missing from snapshot");
                continue;
            }

            for (int level = 1; level <= skill.MaxLevel; level++)
            {
                int want = expected[Math.Min(level, expected.Length) - 1];
                int got = skill.ReuseDelayMsAt(level);
                if (got != want)
                    mismatches.Add($"{skill.SkillId} {skill.Name} lv{level}: {got} != {want}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    [Fact]
    public void Per_level_reuse_table_is_applied_for_variable_skills()
    {
        var anchor = GameData.LoadDefault().Skills.Find(1170);
        Assert.NotNull(anchor);
        Assert.Equal(180_000, anchor!.ReuseDelayMsAt(1));
        Assert.Equal(180_000, anchor.ReuseDelayMsAt(2));
        Assert.Equal(120_000, anchor.ReuseDelayMsAt(3));
    }
}
