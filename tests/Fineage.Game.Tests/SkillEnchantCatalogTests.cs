using Fineage.Game;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class SkillEnchantCatalogTests
{
    [Fact]
    public void Default_catalog_loads_triple_slash_enchant_route()
    {
        var data = GameData.LoadDefault();
        var route = data.SkillEnchants.Find(1, 101);
        Assert.NotNull(route);
        Assert.Equal(37, route.MinSkillLevel);
        Assert.Equal(306_000, route.Sp);
        Assert.True(route.Rate76 > 0);
    }

    [Fact]
    public void Available_returns_next_route_when_min_level_matches()
    {
        var data = GameData.LoadDefault();
        var known = new Dictionary<int, int> { [1] = 37 };
        var offers = data.SkillEnchants.Available(known, playerLevel: 76);
        Assert.Contains(offers, o => o.SkillId == 1 && o.Level == 101);
    }
}