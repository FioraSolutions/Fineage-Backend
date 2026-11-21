using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class PetCatalogTests
{
    [Fact]
    public void Default_catalog_marks_striders_mountable_and_hatchlings_not()
    {
        var cats = PetCatalog.LoadDefault();
        var hatch = cats.ByItem(3500);
        var strider = cats.ByItem(4422);
        Assert.NotNull(hatch);
        Assert.NotNull(strider);
        Assert.False(hatch!.Mountable);
        Assert.Equal(PetMountKind.None, hatch.MountKind);
        Assert.True(strider!.Mountable);
        Assert.Equal(PetMountKind.Strider, strider.MountKind);
    }
}