using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class HennaCatalogTests
{
    [Fact]
    public void LoadDefault_has_dye_1_str_plus_con_minus()
    {
        var catalog = HennaCatalog.LoadDefault();
        Assert.True(catalog.Count >= 180);
        Assert.True(catalog.TryGet(1, out var dye));
        Assert.Equal(1, dye.Str);
        Assert.Equal(-3, dye.Con);
        Assert.Equal(0, dye.Dex);
    }

    [Fact]
    public void Sum_stacks_three_slots()
    {
        var catalog = HennaCatalog.LoadDefault();
        var sum = catalog.Sum([1, 2, 0]);
        Assert.Equal(2, sum.Str); // +1 +1
        Assert.Equal(-3, sum.Con);
        Assert.Equal(-3, sum.Dex);
    }
}
