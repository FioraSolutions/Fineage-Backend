using Fineage.Protocol;
using Fineage.Zone.Punishment;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SevenSignsFestivalTests : IDisposable
{
    readonly string? _prevEnv;

    public SevenSignsFestivalTests()
    {
        _prevEnv = Environment.GetEnvironmentVariable(ZoneAdmin.EnvVar);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
        ZoneAdmin.SetNamesForTest(["KuK"]);
    }

    public void Dispose()
    {
        ZoneAdmin.SetNamesForTest(null);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, _prevEnv);
    }

    static readonly DateTimeOffset Start = new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_loads_five_mobius_tiers()
    {
        var catalog = SevenSignsFestivalCatalog.LoadDefault();
        Assert.Equal(5901, catalog.OfferingItemId);
        Assert.Equal(5, catalog.OfferingValue);
        Assert.Equal(5, catalog.LevelScores.Count);
        Assert.Equal("31", catalog.Tier(0).LevelLabel);
    }

    [Fact]
    public async Task Festival_entry_registers_participation_and_awards_score()
    {
        var catalog = SevenSignsFestivalCatalog.LoadDefault();
        var festival = new SevenSignsFestivalRuntime(catalog);
        var clock = new MutableGameClock(Start.AddMinutes(15));
        var engine = new SevenSignsEngine(Start, festival: festival);
        festival.BindEngine(engine);
        var notices = new List<SevenSignsNotice>();
        Assert.True(engine.TryAdvance(Start.AddMinutes(15), notices));
        Assert.Equal(SevenSignsPeriod.Competition, engine.Period);

        var bench = new WorldBench(clock: () => clock.UtcNow, sevenSigns: engine, festivalRuntime: festival);
        var leader = await bench.EnterAsync(name: "KuK");
        var mate = await bench.EnterAsync(name: "Mate");

        bench.World.JoinPartyForTest(leader.Player, mate.Player);
        Assert.Null(await bench.World.TryGiveItemAsync(leader.Session, catalog.OfferingItemId, 5, default));
        Assert.Null(await bench.World.TryGiveItemAsync(mate.Session, catalog.OfferingItemId, 5, default));

        Assert.Null(await leader.BypassAsync(0, "admin_festival_enter 0 dusk"));
        Assert.True(PlayerZoneChecks.IsInFestivalArena(bench.World, leader.ObjectId));
        Assert.True(PlayerZoneChecks.IsInFestivalArena(bench.World, mate.ObjectId));
        Assert.True(leader.Player.InstanceId >= ZoneWorld.FestivalInstanceBase);

        Assert.Null(await leader.BypassAsync(0, "admin_festival_end"));
        Assert.False(PlayerZoneChecks.IsInFestivalArena(bench.World, leader.ObjectId));
        Assert.True(festival.Board.IsPreviousLeader(SevenSignsCabal.Dusk, 0, leader.ObjectId));

        Assert.Null(await bench.World.TryGiveItemAsync(leader.Session, catalog.OfferingItemId, 50, default));
        int duskBefore = engine.DuskFestivalScore();
        int offerings = leader.Player.Inventory!.CountOf(catalog.OfferingItemId);
        long offeringScore = offerings * (long)catalog.OfferingValue;
        Assert.True(festival.TryRecordInstanceScore(SevenSignsCabal.Dusk, 0, offeringScore));
        Assert.Equal(duskBefore + catalog.LevelScores[0], engine.DuskFestivalScore());
    }
}