using Fineage.Game.Services;
using Xunit;
namespace Fineage.Game.Tests;

public sealed class NpcScriptRegistryTests
{
    [Fact]
    public void LoadDefault_knows_html_script_names()
    {
        var r = NpcScriptRegistry.LoadDefault();
        Assert.True(r.Knows("FirstClassTransferTalk"));
        Assert.False(r.Knows("TotallyFakeScript"));
    }
}
