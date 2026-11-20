using Fineage.Zone.Combat;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>A player walking across a map tile border is handed to the next region at the end-of-tick barrier.</summary>
public sealed class RegionHandoffWorldTests
{
    /// <summary>The west edge of tile 22_19, east of Elven Village (21_19).</summary>
    const int TileEdgeX = (22 - GridRegionPartition.MapTileOffsetX) * GridRegionPartition.MapTileSize;
    const int HandoffX = TileEdgeX + GridRegionPartition.MapTileHandoffMargin;

    static readonly RegionId Village = new(0, 21, 19);
    static readonly RegionId East = new(0, 22, 19);

    [Fact]
    public async Task Walking_east_hands_the_player_off_past_the_margin_and_regen_follows_the_new_owner()
    {
        var observer = new RecordingObserver();
        var kit = new NetTestKit(regions: GridRegionPartition.MapTiles, simulationObserver: observer);
        var (_, resting) = await kit.EnterAsync();
        var (walkerSession, walker) = await kit.EnterAsync(x: TileEdgeX - 200);
        kit.World.NetBridge = NetBridgeRecorder.Create().Item1;
        foreach (var player in new[] { resting, walker })
        {
            player.CurHp = 1;
            kit.World.SyncNeedRegen(player);
        }

        Assert.Equal(new RegionPlacement(Village, 0), kit.World.RegionOwnerOf(walker.ObjectId));
        await kit.World.RunRegenTickAsync(CancellationToken.None);
        Assert.Equal(1, observer.Regions["regen.players"]);

        Assert.Null(await kit.World.TryMoveAsync(walkerSession, TileEdgeX + 900, walker.Y, walker.Z, CancellationToken.None));
        bool heldInsideMargin = false;
        for (int step = 0; step < 200 && walker.MoveActive; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(ZoneCombat.AutoAttackTickMs);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);

            kit.World.VerifyRegions();
            var owner = kit.World.RegionOwnerOf(walker.ObjectId);
            if (walker.X < HandoffX)
            {
                Assert.Equal(new RegionPlacement(Village, 0), owner);
                heldInsideMargin |= walker.X >= TileEdgeX;
            }
            else
            {
                Assert.Equal(new RegionPlacement(East, 1), owner);
            }
        }

        Assert.False(walker.MoveActive);
        Assert.True(heldInsideMargin, "the walk never stopped between the tile edge and the handoff margin");
        Assert.Equal([new RegionTransfer(walker.ObjectId, Village, East, 1)],
            observer.Handoffs.Where(t => t.ObjectId == walker.ObjectId));

        await kit.World.RunRegenTickAsync(CancellationToken.None);
        Assert.Equal(2, observer.Regions["regen.players"]);

        // Walking back inside the margin keeps the new owner.
        Assert.Null(await kit.World.TryMoveAsync(walkerSession, TileEdgeX - 200, walker.Y, walker.Z, CancellationToken.None));
        for (int step = 0; step < 200 && walker.MoveActive; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(ZoneCombat.AutoAttackTickMs);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
            kit.World.VerifyRegions();
        }

        Assert.Equal(new RegionPlacement(East, 1), kit.World.RegionOwnerOf(walker.ObjectId));
        Assert.Empty(observer.Failures);
    }

    [Fact]
    public async Task Handoffs_are_counted_by_the_region_handoff_metric()
    {
        using var collector = new MetricsCollector();
        long before = collector.Snapshot().RegionHandoffs;
        var kit = new NetTestKit(regions: GridRegionPartition.MapTiles);
        var (session, walker) = await kit.EnterAsync(x: TileEdgeX - 50);
        kit.World.NetBridge = NetBridgeRecorder.Create().Item1;

        Assert.Null(await kit.World.TryMoveAsync(session, HandoffX + 200, walker.Y, walker.Z, CancellationToken.None));
        for (int step = 0; step < 50 && walker.MoveActive; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(ZoneCombat.AutoAttackTickMs);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        }

        Assert.Equal(East, kit.World.RegionOwnerOf(walker.ObjectId).Region);
        Assert.True(collector.Snapshot().RegionHandoffs >= before + 1);
    }

    [Fact]
    public async Task A_snapped_move_keeps_the_owner_until_the_next_step_ends()
    {
        var kit = new NetTestKit(regions: GridRegionPartition.MapTiles);
        var (session, walker) = await kit.EnterAsync(x: TileEdgeX - 200);

        Assert.Null(await kit.World.TryMoveAsync(session, HandoffX + 100, walker.Y, walker.Z, CancellationToken.None));
        Assert.Equal(HandoffX + 100, walker.X);
        Assert.Equal(new RegionPlacement(Village, 0), kit.World.RegionOwnerOf(walker.ObjectId));
        kit.World.VerifyRegions();

        kit.World.StepSimulation();

        Assert.Equal(new RegionPlacement(East, 1), kit.World.RegionOwnerOf(walker.ObjectId));
        kit.World.VerifyRegions();
    }

    [Fact]
    public async Task Leaving_the_world_removes_the_owner_and_every_membership()
    {
        var kit = new NetTestKit(regions: GridRegionPartition.MapTiles);
        var (session, player) = await kit.EnterAsync();
        player.CurHp = 1;
        kit.World.SyncNeedRegen(player);
        int needRegen = kit.World.ActiveSetCounts.NeedRegen;

        await kit.World.LeaveAsync(session, CancellationToken.None);

        var error = Assert.Throws<KeyNotFoundException>(() => kit.World.RegionOwnerOf(player.ObjectId));
        Assert.Contains($"entity {player.ObjectId} is not in the world", error.Message);
        Assert.Equal(needRegen - 1, kit.World.ActiveSetCounts.NeedRegen);
        kit.World.VerifyRegions();
    }

    sealed class RecordingObserver : ISimulationObserver
    {
        public Dictionary<string, int> Regions { get; } = [];
        public List<RegionTransfer> Handoffs { get; } = [];
        public List<StageFailure> Failures { get; } = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed) =>
            Regions[stage] = regions;

        public void StageFailed(StageFailure failure) => Failures.Add(failure);

        public void TicksSkipped(TickContext tick, long skipped)
        {
        }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers) => Handoffs.AddRange(transfers);

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
        {
        }
    }
}
