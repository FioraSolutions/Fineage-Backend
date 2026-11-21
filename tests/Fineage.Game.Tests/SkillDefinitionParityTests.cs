using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// data/skills.json fields for the audited class families must match the Mobius CT_0 Interlude
/// snapshot (generated from XML tables, not copied as source). Fields Mobius does not publish
/// (for example an omitted castRange) are left off the snapshot and are not compared, so Fineage
/// self-skill defaults and official visual patches are not forced to zero.
/// </summary>
public sealed class SkillDefinitionParityTests
{
    sealed class Row
    {
        public int SkillId { get; set; }
        public string OperateType { get; set; } = "";
        public string TargetType { get; set; } = "";
        public int? CastRange { get; set; }
        public int? EffectRange { get; set; }
        public int? HitTimeMs { get; set; }
        public JsonElement ReuseDelayMs { get; set; }
        public int MagicType { get; set; }
        public bool IsDebuff { get; set; }
        public string AbnormalType { get; set; } = "";
        public string AbnormalVisualEffect { get; set; } = "";
        public string Trait { get; set; } = "";
        public string BasicProperty { get; set; } = "";
        public int? ActivateRate { get; set; }
        public int? LvlBonusRate { get; set; }
        public int? AbnormalTimeSeconds { get; set; }
        public string[]? UsingKinds { get; set; }
        public int[]? EffectPoint { get; set; }
        public int[]? MpConsume { get; set; }
        public int[]? Power { get; set; }
        public int[]? MagicLevel { get; set; }
        public int[]? MpInitialConsume { get; set; }
    }

    sealed class FileDto
    {
        public List<Row>? Skills { get; set; }
    }

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Skills_json_matches_generated_mobius_definition_snapshot()
    {
        string snapPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "skill-definition-snapshot.json");
        Assert.True(File.Exists(snapPath), snapPath);
        var snapshot = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(snapPath), JsonOptions)?.Skills
            ?? throw new InvalidDataException("empty snapshot");
        Assert.NotEmpty(snapshot);

        var catalog = GameData.LoadDefault().Skills;
        var mismatches = new List<string>();

        foreach (var row in snapshot)
        {
            var skill = catalog.Find(row.SkillId);
            if (skill is null)
            {
                mismatches.Add($"skill {row.SkillId} missing from skills.json");
                continue;
            }

            Cmp(mismatches, row.SkillId, "operateType", skill.OperateType, row.OperateType);
            Cmp(mismatches, row.SkillId, "targetType", skill.TargetType, row.TargetType);
            if (row.CastRange is int cast)
                Cmp(mismatches, row.SkillId, "castRange", skill.CastRange, cast);
            if (row.EffectRange is int effect)
                Cmp(mismatches, row.SkillId, "effectRange", skill.EffectRange, effect);
            if (row.HitTimeMs is int hit)
                Cmp(mismatches, row.SkillId, "hitTimeMs", skill.HitTimeMs, hit);

            if (row.ReuseDelayMs.ValueKind == JsonValueKind.Number)
                Cmp(mismatches, row.SkillId, "reuseDelayMs", skill.ReuseDelayMs, row.ReuseDelayMs.GetInt32());
            else if (row.ReuseDelayMs.ValueKind == JsonValueKind.Array)
            {
                int[] want = row.ReuseDelayMs.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                for (int level = 1; level <= skill.MaxLevel && level <= want.Length; level++)
                {
                    if (skill.ReuseDelayMsAt(level) != want[level - 1])
                        mismatches.Add($"{row.SkillId} reuseDelayMs lv{level} {skill.ReuseDelayMsAt(level)} != {want[level - 1]}");
                }
            }

            Cmp(mismatches, row.SkillId, "magicType", skill.MagicType, row.MagicType);
            Cmp(mismatches, row.SkillId, "isDebuff", skill.IsDebuff, row.IsDebuff);
            Cmp(mismatches, row.SkillId, "abnormalType", skill.AbnormalType ?? "", row.AbnormalType ?? "");
            if (!string.IsNullOrEmpty(row.AbnormalVisualEffect))
                Cmp(mismatches, row.SkillId, "abnormalVisualEffect", skill.AbnormalVisualEffect ?? "", row.AbnormalVisualEffect);
            Cmp(mismatches, row.SkillId, "trait", skill.Trait ?? "", row.Trait ?? "");
            Cmp(mismatches, row.SkillId, "basicProperty", skill.BasicProperty ?? "", row.BasicProperty ?? "");
            if (row.ActivateRate is int ar)
                Cmp(mismatches, row.SkillId, "activateRate", skill.ActivateRate, ar);
            if (row.LvlBonusRate is int lb)
                Cmp(mismatches, row.SkillId, "lvlBonusRate", skill.LvlBonusRate, lb);
            if (row.AbnormalTimeSeconds is int abn)
                Cmp(mismatches, row.SkillId, "abnormalTimeSeconds", skill.AbnormalTimeSeconds, abn);

            if (skill.CastCondition == null)
            {
                var gotKinds = (skill.UsingKinds ?? []).ToArray();
                var wantKinds = row.UsingKinds ?? [];
                if (!gotKinds.SequenceEqual(wantKinds))
                    mismatches.Add($"{row.SkillId} usingKinds [{string.Join(",", gotKinds)}] != [{string.Join(",", wantKinds)}]");
            }

            if (row.EffectPoint is { Length: > 0 } points)
            {
                var got = skill.EffectPoints.ToArray();
                if (!got.SequenceEqual(points))
                    mismatches.Add($"{row.SkillId} effectPoint mismatch");
            }

            CompareLevels(mismatches, skill, "mpConsume", row.MpConsume, d => d.MpConsume);
            CompareLevels(mismatches, skill, "power", row.Power, d => d.Power);
            CompareLevels(mismatches, skill, "magicLevel", row.MagicLevel, d => d.MagicLevel);
            CompareLevels(mismatches, skill, "mpInitialConsume", row.MpInitialConsume, d => d.MpInitialConsume);
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    static void CompareLevels(List<string> mismatches, SkillDefinition skill, string field, int[]? want, Func<SkillLevelData, int> read)
    {
        if (want is not { Length: > 0 })
            return;
        for (int level = 1; level <= skill.MaxLevel && level <= want.Length; level++)
        {
            if (!skill.TryLevelData(level, out var data))
            {
                mismatches.Add($"{skill.SkillId} {field} lv{level} missing");
                continue;
            }
            if (read(data) != want[level - 1])
                mismatches.Add($"{skill.SkillId} {field} lv{level} {read(data)} != {want[level - 1]}");
        }
    }

    static void Cmp<T>(List<string> mismatches, int skillId, string field, T got, T want)
    {
        if (!EqualityComparer<T>.Default.Equals(got, want))
            mismatches.Add($"{skillId} {field} {got} != {want}");
    }
}
