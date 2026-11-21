using System.Text.Json;
using Fineage.Game.Skills;
using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// data/skill-combat.json rows for the audited class families must match the Mobius CT_0 Interlude
/// snapshot (generated from XML tables, including a fallback to a #power table when the skill omits
/// a &lt;power&gt; element). Cast range is only compared when Mobius publishes one, so Fineage AoE
/// sentinels of -1 are not forced to 0.
/// </summary>
public sealed class SkillCombatParityTests
{
    sealed class Row
    {
        public int SkillId { get; set; }
        public int Levels { get; set; }
        public bool Magic { get; set; }
        public double[] Power { get; set; } = [];
        public int[] MpConsume { get; set; } = [];
        public int[] MpInitialConsume { get; set; } = [];
        public int HitTimeMs { get; set; }
        public int CoolTimeMs { get; set; }
        public int ReuseDelayMs { get; set; }
        public int? CastRange { get; set; }
        public int? EffectRange { get; set; }
        public string[]? UsingKinds { get; set; }
        public int[]? MagicLevel { get; set; }
        public bool OverHit { get; set; }
    }

    sealed class FileDto
    {
        public List<Row>? Skills { get; set; }
    }

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Skill_combat_catalog_matches_generated_mobius_snapshot()
    {
        string snapPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "skill-combat-snapshot.json");
        Assert.True(File.Exists(snapPath), snapPath);
        var snapshot = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(snapPath), JsonOptions)?.Skills
            ?? throw new InvalidDataException("empty snapshot");
        Assert.NotEmpty(snapshot);

        var catalog = SkillCombatCatalog.LoadDefault();
        var defs = SkillCatalog.LoadDefault();
        var mismatches = new List<string>();

        foreach (var row in snapshot)
        {
            if (!catalog.TryGet(row.SkillId, out var got))
            {
                mismatches.Add($"skill {row.SkillId} missing from skill-combat.json");
                continue;
            }

            if (got.Levels != row.Levels)
                mismatches.Add($"{row.SkillId} levels {got.Levels} != {row.Levels}");
            if (got.Magic != row.Magic)
                mismatches.Add($"{row.SkillId} magic {got.Magic} != {row.Magic}");
            if (got.HitTimeMs != row.HitTimeMs)
                mismatches.Add($"{row.SkillId} hitTimeMs {got.HitTimeMs} != {row.HitTimeMs}");
            if (got.CoolTimeMs != row.CoolTimeMs)
                mismatches.Add($"{row.SkillId} coolTimeMs {got.CoolTimeMs} != {row.CoolTimeMs}");
            if (got.ReuseDelayMs != row.ReuseDelayMs)
                mismatches.Add($"{row.SkillId} reuseDelayMs {got.ReuseDelayMs} != {row.ReuseDelayMs}");
            if (row.CastRange is int cast && got.CastRange != cast)
                mismatches.Add($"{row.SkillId} castRange {got.CastRange} != {cast}");
            if (row.EffectRange is int effect && got.EffectRange != effect)
                mismatches.Add($"{row.SkillId} effectRange {got.EffectRange} != {effect}");

            if (defs.Find(row.SkillId) is not { CastCondition: not null })
            {
                var wantKinds = row.UsingKinds ?? [];
                if (!got.UsingKinds.SequenceEqual(wantKinds))
                    mismatches.Add($"{row.SkillId} usingKinds [{string.Join(",", got.UsingKinds)}] != [{string.Join(",", wantKinds)}]");
            }

            CompareDoubles(mismatches, row.SkillId, "power", got.Power, row.Power);
            CompareInts(mismatches, row.SkillId, "mpConsume", got.MpConsume, row.MpConsume);
            CompareInts(mismatches, row.SkillId, "mpInitialConsume", got.MpInitialConsume, row.MpInitialConsume);
            if (row.MagicLevel is { Length: > 0 })
                CompareInts(mismatches, row.SkillId, "magicLevel", got.MagicLevel, row.MagicLevel);
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    static void CompareInts(List<string> mismatches, int skillId, string field, int[] got, int[] want)
    {
        if (got.Length != want.Length || !got.SequenceEqual(want))
            mismatches.Add($"{skillId} {field} mismatch");
    }

    static void CompareDoubles(List<string> mismatches, int skillId, string field, double[] got, double[] want)
    {
        if (got.Length != want.Length)
        {
            mismatches.Add($"{skillId} {field} length {got.Length} != {want.Length}");
            return;
        }
        for (int i = 0; i < want.Length; i++)
        {
            if (Math.Abs(got[i] - want[i]) > 0.001)
            {
                mismatches.Add($"{skillId} {field}[{i}] {got[i]} != {want[i]}");
                return;
            }
        }
    }
}
