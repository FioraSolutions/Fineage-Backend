using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class GmTeleportCatalogTests
{
    [Fact]
    public void LoadDefault_includes_interlude_towns()
    {
        var catalog = GmTeleportCatalog.LoadDefault();
        Assert.True(catalog.Towns.Count >= 17);
        var gludio = catalog.Find("gludio");
        Assert.NotNull(gludio);
        Assert.Equal(-12787, gludio!.X);
        Assert.Equal(122779, gludio.Y);
        Assert.Equal(-3114, gludio.Z);
        Assert.Same(gludio, catalog.Find("GLUDIO"));
    }

    [Fact]
    public void Parse_builds_alias_index()
    {
        const string json = """
            {
              "towns": [
                { "keys": ["ti", "talking_island"], "name": "Talking Island Village", "x": 1, "y": 2, "z": 3 }
              ]
            }
            """;
        var catalog = GmTeleportCatalog.Parse(json, "test");
        Assert.Equal("ti", catalog.Find("talking_island")!.PrimaryKey);
        Assert.Equal("Talking Island Village", catalog.Find("ti")!.DisplayName);
    }
}