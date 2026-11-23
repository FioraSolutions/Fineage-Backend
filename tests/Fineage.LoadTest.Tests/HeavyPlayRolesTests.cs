using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Models;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class HeavyPlayRolesTests
{
    [Theory]
    [InlineData(0, HeavyPlayRole.TrainWarlord)]
    [InlineData(3, HeavyPlayRole.TrainObserver)]
    public void Aoe_train_roles(int botId, HeavyPlayRole expected)
    {
        var scenario = new LoadScenario
        {
            HeavyPlay = new HeavyPlayConfig { Mode = "aoe-train", PartySize = 9 }
        };
        Assert.Equal(expected, HeavyPlayRoles.Resolve(scenario, botId));
    }

    [Fact]
    public void Multi_party_leaders_are_warlords()
    {
        var scenario = new LoadScenario
        {
            HeavyPlay = new HeavyPlayConfig { Mode = "aoe-multiparty", PartySize = 9, PartyCount = 4 }
        };
        Assert.Equal(HeavyPlayRole.PartyWarlord, HeavyPlayRoles.Resolve(scenario, 0));
        Assert.Equal(HeavyPlayRole.PartyWarlord, HeavyPlayRoles.Resolve(scenario, 9));
        Assert.Equal(HeavyPlayRole.PartyObserver, HeavyPlayRoles.Resolve(scenario, 1));
    }
}