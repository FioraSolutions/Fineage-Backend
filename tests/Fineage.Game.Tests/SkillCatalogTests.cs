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
}
