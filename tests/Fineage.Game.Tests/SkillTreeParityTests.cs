using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// Class skill-tree rows in data/skill-trees.json must match the Mobius CT_0 Interlude snapshot
/// (generated from the XML tables, not copied as source). The snapshot grows family by family;
/// every class it lists is compared for membership, getLevel, autoGet, learnedByNpc, levelUpSp,
/// requiredItems, and preReqSkills.
/// </summary>
public sealed class SkillTreeParityTests
{
    sealed class Row
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int GetLevel { get; set; }
        public bool AutoGet { get; set; }
        public bool LearnedByNpc { get; set; }
        public int LevelUpSp { get; set; }
        public ItemRow[]? RequiredItems { get; set; }
        public PrereqRow[]? PreReqSkills { get; set; }
    }

    sealed class ItemRow
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
    }

    sealed class PrereqRow
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Skill_trees_match_generated_mobius_snapshot()
    {
        string snapPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "skill-tree-snapshot.json");
        Assert.True(File.Exists(snapPath), snapPath);
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, List<Row>>>(File.ReadAllText(snapPath), JsonOptions)
            ?? throw new InvalidDataException("empty snapshot");
        Assert.NotEmpty(snapshot);

        var data = GameData.LoadDefault();
        var mismatches = new List<string>();

        foreach (var (key, expected) in snapshot.OrderBy(e => int.Parse(e.Key)))
        {
            int classId = int.Parse(key);
            var tree = data.SkillTrees.FindClass(classId);
            if (tree is null)
            {
                mismatches.Add($"class {classId} missing from skill-trees.json");
                continue;
            }

            var got = tree.Skills.ToDictionary(s => (s.SkillId, s.Level));
            var want = expected.ToDictionary(s => (s.SkillId, s.Level));

            foreach (var id in want.Keys.Except(got.Keys).OrderBy(k => k))
                mismatches.Add($"class {classId} missing {id.SkillId} lv{id.Level}");
            foreach (var id in got.Keys.Except(want.Keys).OrderBy(k => k))
                mismatches.Add($"class {classId} extra {id.SkillId} lv{id.Level}");

            foreach (var (id, row) in want)
            {
                if (!got.TryGetValue(id, out var entry))
                    continue;
                if (entry.GetLevel != row.GetLevel)
                    mismatches.Add($"class {classId} {id.SkillId} lv{id.Level} getLevel {entry.GetLevel} != {row.GetLevel}");
                if (entry.AutoGet != row.AutoGet)
                    mismatches.Add($"class {classId} {id.SkillId} lv{id.Level} autoGet {entry.AutoGet} != {row.AutoGet}");
                if (entry.LearnedByNpc != row.LearnedByNpc)
                    mismatches.Add($"class {classId} {id.SkillId} lv{id.Level} learnedByNpc {entry.LearnedByNpc} != {row.LearnedByNpc}");
                if (entry.LevelUpSp != row.LevelUpSp)
                    mismatches.Add($"class {classId} {id.SkillId} lv{id.Level} sp {entry.LevelUpSp} != {row.LevelUpSp}");

                var gotItems = entry.Items.Select(i => (i.ItemId, i.Count)).ToArray();
                var wantItems = (row.RequiredItems ?? []).Select(i => (i.ItemId, i.Count)).ToArray();
                if (!gotItems.SequenceEqual(wantItems))
                    mismatches.Add($"class {classId} {id.SkillId} lv{id.Level} items {Fmt(gotItems)} != {Fmt(wantItems)}");

                var gotPre = entry.Prerequisites.Select(p => (p.SkillId, p.Level)).ToArray();
                var wantPre = (row.PreReqSkills ?? []).Select(p => (p.SkillId, p.Level)).ToArray();
                if (!gotPre.SequenceEqual(wantPre))
                    mismatches.Add($"class {classId} {id.SkillId} lv{id.Level} prereq {Fmt(gotPre)} != {Fmt(wantPre)}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    [Fact]
    public void Snapshot_learn_items_exist_in_the_item_catalog()
    {
        string snapPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "skill-tree-snapshot.json");
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, List<Row>>>(File.ReadAllText(snapPath), JsonOptions)
            ?? throw new InvalidDataException("empty snapshot");
        var items = GameData.LoadDefault().Items;
        var missing = new List<string>();
        foreach (var (key, rows) in snapshot)
        {
            foreach (var row in rows)
            {
                foreach (var item in row.RequiredItems ?? [])
                {
                    if (!items.TryGet(item.ItemId, out _))
                        missing.Add($"class {key} skill {row.SkillId} item {item.ItemId}");
                }
            }
        }

        Assert.True(missing.Count == 0, string.Join("\n", missing.Take(20)));
    }

    static string Fmt<T>(IEnumerable<T> rows) => "[" + string.Join(",", rows) + "]";
}
