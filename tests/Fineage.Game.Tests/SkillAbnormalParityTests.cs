using System.Text.Json;
using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// data/skills.json <c>abnormalType</c> and per-level <c>abnormalLevel</c> must match the Mobius
/// CT_0 Interlude snapshot (generated from XML tables, not copied as source).
/// </summary>
public sealed class SkillAbnormalParityTests
{
    sealed class Row
    {
        public string Type { get; set; } = "";
        public int[] Levels { get; set; } = [];
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Skills_json_abnormal_types_match_generated_mobius_snapshot()
    {
        string snapPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "skill-abnormal-snapshot.json");
        Assert.True(File.Exists(snapPath), snapPath);
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, Row>>(File.ReadAllText(snapPath), JsonOptions)
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

            string gotType = skill.AbnormalType ?? "";
            if (!string.Equals(gotType, expected.Type ?? "", StringComparison.OrdinalIgnoreCase))
                mismatches.Add($"{skill.SkillId} {skill.Name} type '{gotType}' != '{expected.Type}'");

            // Typeless skills have no stack slot. Our datapack often writes abnormalLevel 1 as a
            // row default; Mobius leaves the field off (0). Compare levels only when a slot exists
            // or Mobius publishes a non-zero value.
            int[] want = expected.Levels ?? [];
            bool compareLevels = !string.IsNullOrEmpty(expected.Type) || want.Any(v => v != 0);
            if (!compareLevels)
                continue;

            for (int level = 1; level <= skill.MaxLevel; level++)
            {
                if (!skill.TryLevelData(level, out var data))
                {
                    mismatches.Add($"{skill.SkillId} {skill.Name} lv{level}: missing level data");
                    continue;
                }

                int expectedLevel = want.Length == 0 ? 0 : want[Math.Min(level, want.Length) - 1];
                if (data.AbnormalLevel != expectedLevel)
                    mismatches.Add($"{skill.SkillId} {skill.Name} lv{level}: abnormalLevel {data.AbnormalLevel} != {expectedLevel}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    [Fact]
    public void Focus_dagger_toggles_share_one_slot()
    {
        var skills = GameData.LoadDefault().Skills;
        foreach (int id in new[] { 355, 356, 357 })
        {
            var skill = skills.Find(id);
            Assert.NotNull(skill);
            Assert.Equal("FOCUS_DAGGER", skill!.AbnormalType);
            Assert.True(skill.TryLevelData(1, out var lv));
            Assert.Equal(1, lv.AbnormalLevel);
        }
    }
}
