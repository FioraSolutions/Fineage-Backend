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
    public void Every_party_slot_fights()
    {
        var scenario = new LoadScenario
        {
            HeavyPlay = new HeavyPlayConfig { Mode = "aoe-multiparty", PartySize = 9, PartyCount = 4 }
        };
        for (int bot = 0; bot < 36; bot++)
        {
            Assert.Equal(HeavyPlayRole.PartyWarlord, HeavyPlayRoles.Resolve(scenario, bot));
            Assert.True(HeavyPlayRoles.IsCombatKiller(HeavyPlayRoles.Resolve(scenario, bot)));
        }

        Assert.Equal(HeavyPlayRole.MixedRandom, HeavyPlayRoles.Resolve(scenario, 36));
    }

    [Fact]
    public void Combined_background_bots_run_the_economy_mix_and_only_they_travel()
    {
        string dir = ScenarioDir();
        var s = ScenarioLoader.Load(Path.Combine(dir, "perf-aoe-combined.yaml"));
        Assert.Equal(HeavyPlayRole.PartyWarlord, HeavyPlayRoles.Resolve(s, 35));
        Assert.Equal(HeavyPlayRole.EconomyBackground, HeavyPlayRoles.Resolve(s, 36));
        Assert.DoesNotContain(Enumerable.Range(0, 36), s.IsGatekeeperTraveler);
        int travellers = Enumerable.Range(36, 36).Count(s.IsGatekeeperTraveler);
        Assert.Equal(7, travellers); // floor(36 × 0.20)
    }

    internal static string ScenarioDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "Fineage.LoadTest", "scenarios")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tools", "Fineage.LoadTest", "scenarios");
    }
}