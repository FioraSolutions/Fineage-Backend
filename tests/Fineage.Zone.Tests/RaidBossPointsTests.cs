using Fineage.Zone.RaidBossPoints;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class RaidBossPointsTests
{
    [Fact]
    public void Manager_accumulates_points_and_ranks_totals()
    {
        var manager = new RaidBossPointsManager(RaidBossPointsCatalog.ForTests(topN: 3), new MemoryRaidBossPointsStore());

        manager.AddPoints(1, "Alice", 25001, 40);
        manager.AddPoints(1, "Alice", 25002, 10);
        manager.AddPoints(2, "Bob", 25001, 55);

        Assert.Equal(50, manager.GetTotalPoints(1));
        Assert.Equal(55, manager.GetTotalPoints(2));
        Assert.Equal(1, manager.CalculateRanking(2));

        var top = manager.TopN(3);
        Assert.Equal(2, top.Count);
        Assert.Equal("Bob", top[0].Name);
        Assert.Equal(55, top[0].TotalPoints);
    }

    [Fact]
    public void Points_for_kill_follows_level_formula()
    {
        var rng = new Random(7);
        int a = RaidBossPointsManager.PointsForKill(40, rng);
        int b = RaidBossPointsManager.PointsForKill(40, rng);
        Assert.InRange(a, 15, 25);
        Assert.InRange(b, 15, 25);
    }

    [Fact]
    public async Task Shared_death_pipeline_grants_points_on_raid_kill()
    {
        var manager = new RaidBossPointsManager(RaidBossPointsCatalog.ForTests(), new MemoryRaidBossPointsStore());
        var bench = new WorldBench(raidBossPoints: manager);
        var actor = await bench.EnterAsync("rbphunt1", "Hunter");
        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var player));
        var raid = new ZoneEntity
        {
            Kind = ZoneEntityKind.Npc,
            NpcType = "RaidBoss",
            NpcId = 25001,
            ObjectId = 900_001,
            Level = 30
        };

        bench.World.NoteRaidBossPointsForTest(raid, player);
        Assert.True(manager.GetTotalPoints(player.ObjectId) > 0);
    }

    [Fact]
    public async Task Ranking_bypass_renders_top_n_from_store()
    {
        var manager = new RaidBossPointsManager(RaidBossPointsCatalog.ForTests(topN: 2, 31603), new MemoryRaidBossPointsStore());
        manager.AddPoints(10, "Top", 25001, 90);
        manager.AddPoints(11, "Second", 25001, 50);

        var bench = new WorldBench(raidBossPoints: manager);
        var actor = await bench.EnterAsync("rbpview1", "Viewer");

        string? error = bench.World.RaidBossRankingForTest(actor.Session, actor.Ticket.CharacterId);
        Assert.Null(error);
        string html = bench.World.LastNpcHtmlForTest ?? "";
        Assert.Contains("Top", html);
        Assert.Contains("90", html);
        Assert.Contains("Second", html);
    }
}
