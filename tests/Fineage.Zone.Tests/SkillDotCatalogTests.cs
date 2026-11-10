using Fineage.Zone.Combat;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SkillDotCatalogTests
{
    [Fact]
    public void Loads_poison_percent_and_bleed_flat_from_datapack()
    {
        var dots = SkillDotCatalog.LoadDefault();
        Assert.True(dots.Count > 0);
        Assert.True(dots.TryGet(129, out var poison));
        Assert.True(poison.Percent);
        Assert.Equal(2000, poison.TickMs);
        Assert.Equal(0.009, poison.PowerAt(1), 3);
        Assert.True(dots.TryGet(96, out var bleed));
        Assert.False(bleed.Percent);
        Assert.Equal(5000, bleed.TickMs);
        Assert.Equal(25, bleed.PowerAt(1), 3);
    }
}
