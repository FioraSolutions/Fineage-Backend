using Fineage.Game;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class SkillCatalogTests
{
    [Fact]
    public void Human_fighter_level_1_auto_gets_lucky_and_common_craft()
    {
        var data = GameData.LoadDefault();
        var skills = PlayerSkillBook.AutoSkills(data.SkillTrees, data.Skills, classId: 0, level: 1);
        Assert.Contains(skills, row => row.SkillId == 194 && row.Level == 1);
        Assert.Contains(skills, row => row.SkillId == 1322 && row.Level == 1);
    }

    [Theory]
    [InlineData(94)]
    [InlineData(13)]
    public void Third_class_level_80_includes_skills_from_every_parent_class(int classId)
    {
        var data = GameData.LoadDefault();
        var classes = data.Classes!;
        var known = PlayerSkillBook.AutoSkills(data.SkillTrees, data.Skills, classId, 80, classes)
            .Select(r => r.SkillId).ToHashSet();
        var own = PlayerSkillBook.AutoSkills(data.SkillTrees, data.Skills, classId, 80);
        Assert.True(known.Count > own.Count);
        foreach (int cid in classes.ParentChain(classId))
        {
            var tree = data.SkillTrees.FindClass(cid);
            Assert.NotNull(tree);
            Assert.Contains(tree!.Skills, e => (e.AutoGet || e.LearnedByNpc) && e.GetLevel <= 80 && known.Contains(e.SkillId));
        }
    }

    [Fact]
    public void Power_strike_level_1_data_matches_mobius_first_rank()
    {
        var data = GameData.LoadDefault();
        var skill = data.Skills.Find(3);
        Assert.NotNull(skill);
        var level = skill!.LevelData(1);
        Assert.Equal(10, level.MpConsume);
        Assert.Equal(25, level.Power);
        // Mobius CT0 Interlude Power Strike set reuseDelay="3000".
        Assert.Equal(3000, skill.ReuseDelayMs);
    }

    [Fact]
    public void Wind_walk_abnormal_metadata_matches_mobius()
    {
        var data = GameData.LoadDefault();
        var skill = data.Skills.Find(1204);
        Assert.NotNull(skill);
        Assert.True(skill!.IsBuffSkill);
        Assert.Equal(1200, skill.AbnormalTimeSeconds);
        Assert.Equal(1, skill.LevelData(1).AbnormalLevel);
        Assert.Equal(2, skill.LevelData(2).AbnormalLevel);
    }

    [Fact]
    public void Wind_shackle_is_debuff_with_fifteen_second_abnormal()
    {
        var data = GameData.LoadDefault();
        var skill = data.Skills.Find(1206);
        Assert.NotNull(skill);
        Assert.True(skill!.IsDebuffSkill);
        Assert.Equal(15, skill.AbnormalTimeSeconds);
    }

    /// <summary>
    /// Mobius lists Divine Inspiration (1405) with one Ancient Book per level (8618/8619/8620/8621).
    /// The tree must carry those items so the trainer can list and consume them.
    /// </summary>
    [Fact]
    public void Divine_inspiration_rows_carry_their_ancient_books()
    {
        var data = GameData.LoadDefault();
        var rows = data.SkillTrees.FindClass(25)!.Skills.Where(e => e.SkillId == 1405).ToDictionary(e => e.Level);
        Assert.Equal(8618, Assert.Single(rows[1].Items).ItemId);
        Assert.Equal(8619, Assert.Single(rows[2].Items).ItemId);
        Assert.Equal(8620, Assert.Single(rows[3].Items).ItemId);
        Assert.Equal(8621, Assert.Single(rows[4].Items).ItemId);
        foreach (int level in new[] { 1, 2, 3, 4 })
        {
            Assert.True(data.Items.TryGet(rows[level].Items[0].ItemId, out _));
            Assert.Empty(rows[level].Prerequisites);
        }
    }

    [Fact]
    public void Trainer_check_follows_mobius_order_level_sp_prerequisite_items()
    {
        var data = GameData.LoadDefault();
        var row = new LearnableSkill(1405, 1, 52, 1000,
            [new SkillLearnItem(8618, 1)], [new SkillLearnPrerequisite(239, 2)]);
        var known = new Dictionary<int, int> { [239] = 2 };
        Func<int, int> noBook = _ => 0;
        Func<int, int> withBook = id => id == 8618 ? 1 : 0;

        Assert.Equal(LearnBlock.Level, TrainerSkills.Check(row, 51, 5000, known, withBook));
        Assert.Equal(LearnBlock.Sp, TrainerSkills.Check(row, 52, 999, known, withBook));
        Assert.Equal(LearnBlock.Prerequisite, TrainerSkills.Check(row, 52, 5000, new Dictionary<int, int>(), withBook));
        Assert.Equal(LearnBlock.Items, TrainerSkills.Check(row, 52, 5000, known, noBook));
        Assert.Equal(LearnBlock.None, TrainerSkills.Check(row, 52, 5000, known, withBook));
    }

    [Fact]
    public void Every_tree_item_requirement_names_a_known_item()
    {
        var data = GameData.LoadDefault();
        foreach (var tree in data.SkillTrees.Trees)
        {
            foreach (var entry in tree.Skills)
            {
                foreach (var item in entry.Items)
                {
                    Assert.True(item.Count > 0);
                    Assert.True(data.Items.TryGet(item.ItemId, out _),
                        $"class {tree.ClassId} skill {entry.SkillId} needs unknown item {item.ItemId}");
                }

                foreach (var prereq in entry.Prerequisites)
                    Assert.True(prereq.Level > 0 && prereq.SkillId > 0);
            }
        }
    }
}
