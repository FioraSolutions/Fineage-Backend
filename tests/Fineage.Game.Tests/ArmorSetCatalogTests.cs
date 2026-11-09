using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class ArmorSetCatalogTests
{
    [Fact]
    public void Wooden_set_skills_when_complete()
    {
        var catalog = ArmorSetCatalog.LoadDefault();
        Assert.True(catalog.Count > 0);
        var skills = catalog.ActiveSkills(23, 2386, 43, null, null, null, _ => 0);
        Assert.Contains(3006, skills.Keys);
        Assert.Contains(3500, skills.Keys);
    }

    [Fact]
    public void Incomplete_set_grants_nothing()
    {
        var catalog = ArmorSetCatalog.LoadDefault();
        var skills = catalog.ActiveSkills(23, null, 43, null, null, null, _ => 0);
        Assert.Empty(skills);
    }
}
