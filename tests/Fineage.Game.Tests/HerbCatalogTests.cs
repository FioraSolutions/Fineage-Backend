using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class HerbCatalogTests
{
    [Fact]
    public void Interlude_herbs_map_to_mobius_item_skills()
    {
        var herbs = HerbCatalog.LoadDefault();
        foreach (int id in Enumerable.Range(8600, 15).Append(8952).Append(8953))
            Assert.True(herbs.IsHerb(id), $"{id}");
        Assert.False(herbs.IsHerb(8954)); // Blue Primeval Crystal is not a herb
        Assert.False(herbs.IsHerb(57));

        var life = Assert.Single(herbs.EffectsOf(8600));
        Assert.Equal(HerbKind.HpPercent, life.Skill.Kind);
        Assert.Equal(15, life.Skill.AmountAt(life.Level));
        Assert.Equal(25, herbs.EffectsOf(8601)[0].Skill.AmountAt(2));
        Assert.Equal(18, herbs.EffectsOf(8604)[0].Skill.AmountAt(2));

        Assert.Equal([2280, 2282, 2284], herbs.EffectsOf(8612).Select(e => e.Skill.SkillId));
        Assert.All(herbs.EffectsOf(8612), e => Assert.Equal(300, e.Skill.DurationSeconds));
        Assert.Equal([HerbKind.HpPercent, HerbKind.MpPercent], herbs.EffectsOf(8614).Select(e => e.Skill.Kind));
    }

    [Fact]
    public void Restore_is_capped_at_max()
    {
        Assert.Equal(15, HerbCatalog.RestorePercent(10, 100, 15));
        Assert.Equal(5, HerbCatalog.RestorePercent(95, 100, 15));
        Assert.Equal(0, HerbCatalog.RestorePercent(100, 100, 15));
        Assert.Equal(0, HerbCatalog.RestorePercent(10, 0, 15));
    }
}
