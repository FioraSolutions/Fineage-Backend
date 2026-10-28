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

    [Fact]
    public void Power_strike_level_1_data_matches_mobius_first_rank()
    {
        var data = GameData.LoadDefault();
        var skill = data.Skills.Find(3);
        Assert.NotNull(skill);
        var level = skill!.LevelData(1);
        Assert.Equal(10, level.MpConsume);
        Assert.Equal(25, level.Power);
        Assert.Equal(13000, skill.ReuseDelayMs);
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
}
