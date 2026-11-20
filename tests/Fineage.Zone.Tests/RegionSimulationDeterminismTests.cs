using Fineage.Protocol;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Regen and abnormal/DoT ticks run as per-region stages of the <see cref="RegionTickScheduler"/>. The outcome must
/// not depend on how the world is cut into regions, and two identical worlds must produce identical state and the
/// identical fanout sequence.
/// </summary>
public sealed class RegionSimulationDeterminismTests
{
    const int FirstMobId = 9_700_001;
    const int MobCount = 6;
    const int MobSpacing = 7_000;
    const int BleedSkillId = 96;
    const int Steps = 12;

    [Fact]
    public async Task Identical_worlds_produce_identical_state_and_fanout_sequence()
    {
        var first = await RunAsync(GridRegionPartition.MapTiles);
        var second = await RunAsync(GridRegionPartition.MapTiles);

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Fanout, second.Fanout);
        Assert.NotEmpty(first.Fanout);
    }

    [Fact]
    public async Task Regen_and_dot_outcome_does_not_depend_on_the_region_partition()
    {
        var tiles = await RunAsync(GridRegionPartition.MapTiles);
        var fine = await RunAsync(new GridRegionPartition(500));
        var whole = await RunAsync(new GridRegionPartition(1 << 30));

        Assert.Equal(tiles.State, fine.State);
        Assert.Equal(tiles.State, whole.State);
        Assert.Equal(Sorted(tiles.Fanout), Sorted(fine.Fanout));
        Assert.Equal(Sorted(tiles.Fanout), Sorted(whole.Fanout));
    }

    [Fact]
    public async Task Regen_and_abnormal_stages_are_spread_across_regions()
    {
        var observer = new StageObserver();
        var (kit, _, _) = await SetupAsync(GridRegionPartition.MapTiles, observer);

        await kit.World.RunRegenTickAsync(CancellationToken.None);
        await kit.World.RunAbnormalTickAsync(CancellationToken.None);

        // Elven Village sits on tile 21_19; the eastern mobs and the second player are on 22_19.
        Assert.Equal(2, observer.Regions["regen.players"]);
        Assert.Equal(2, observer.Regions["regen.npcs"]);
        Assert.Equal(2, observer.Regions["abnormals"]);
        Assert.Empty(observer.Failures);
    }

    [Fact]
    public async Task Full_step_runs_every_stage_in_the_legacy_loop_order_without_failures()
    {
        var observer = new StageObserver();
        var (kit, _, _) = await SetupAsync(GridRegionPartition.MapTiles, observer);

        kit.World.StepSimulation();

        string[] legacyOrder =
        [
            "regen.players", "regen.npcs", "spawns", "abnormals", "bosses", "clan.dissolutions", "clan.hall-auctions",
            "sieges", "manor", "fishing", "lottery", "seven-signs", "mammon", "olympiad"
        ];
        Assert.Equal(legacyOrder, kit.World.SimulationTicks.StageNames);
        Assert.Equal(legacyOrder, observer.Completed);
        Assert.Empty(observer.Failures);
        Assert.Equal(1, kit.World.SimulationTicks.Tick);
    }

    static async Task<(List<string> State, List<string> Fanout)> RunAsync(IRegionPartition partition)
    {
        var (kit, recorder, tracked) = await SetupAsync(partition, new StageObserver());
        for (int step = 0; step < Steps; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(Fineage.Zone.Combat.ZoneRegen.DefaultTickMs);
            await kit.World.RunRegenTickAsync(CancellationToken.None);
            await kit.World.RunAbnormalTickAsync(CancellationToken.None);
        }

        var state = new List<string>();
        foreach (int id in tracked)
        {
            Assert.True(kit.World.TryGetEntityById(id, out var e));
            string abnormals = string.Join(",", e.Abnormals.Select(a => $"{a.Id}:{a.ExpiresAtUnixMs}:{a.NextDotAtUnixMs}"));
            state.Add($"{id} hp={e.CurHp} mp={e.CurMp} cp={e.CurCp} " +
                      $"carry={e.RegenCarryHp:R}/{e.RegenCarryMp:R}/{e.RegenCarryCp:R} ab=[{abnormals}]");
        }

        List<string> fanout;
        lock (recorder.Calls)
            fanout = recorder.Calls.Select(Describe).ToList();
        return (state, fanout);
    }

    static async Task<(NetTestKit Kit, NetBridgeRecorder Recorder, int[] Tracked)> SetupAsync(
        IRegionPartition partition, ISimulationObserver observer)
    {
        var kit = new NetTestKit(Map(), "determinism", regions: partition, simulationObserver: observer);
        var (_, west) = await kit.EnterAsync();
        var (_, east) = await kit.EnterAsync(x: NetTestKit.ElfX + 21_000);
        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        var bleed = kit.Game.Skills.Find(BleedSkillId);
        Assert.NotNull(bleed);
        Assert.True(bleed!.TryLevelData(1, out var bleedLevel));

        foreach (var player in new[] { west, east })
        {
            player.CurHp = Math.Max(1, player.MaxHp / 3);
            player.CurMp = Math.Max(1, player.MaxMp / 4);
            player.CurCp = Math.Max(0, player.MaxCp / 2);
            kit.World.SyncNeedRegen(player);
        }
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(bleed, bleedLevel, east));

        for (int i = 0; i < MobCount; i++)
        {
            Assert.True(kit.World.TryGetEntityById(FirstMobId + i, out var mob));
            mob.CurHp = mob.MaxHp / 3;
            mob.CurMp = 1;
            kit.World.SyncNeedRegen(mob);
            if (i % 3 == 0)
                Assert.True(kit.World.ApplyAbnormalFromSkillForTest(bleed, bleedLevel, mob));
        }

        lock (recorder.Calls)
            recorder.Calls.Clear();
        int[] tracked = [.. new[] { west.ObjectId, east.ObjectId }.Concat(Enumerable.Range(FirstMobId, MobCount)).Order()];
        return (kit, recorder, tracked);
    }

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["determinism"] = new("determinism", true,
                Enumerable.Range(0, MobCount).Select(i =>
                    new SpawnNpcDefinition(FirstMobId + i, 97000 + i, "Gremlin", NetTestKit.ElfX + 30 + i * MobSpacing,
                        NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 400, 1f, 1f, 60, 0, "", "Monster",
                        Level: 1 + i, HpRegen: 2.5 + i * 0.35, MpRegen: 1.2 + i * 0.15, MaxMp: 100,
                        AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []))
                    .ToList())
        });

    static List<string> Sorted(List<string> calls) => calls.Order(StringComparer.Ordinal).ToList();

    static string Describe((string Method, object?[] Args) call) =>
        call.Method + "(" + string.Join(" | ", call.Args.Select(DescribeArg)) + ")";

    static string DescribeArg(object? arg) => arg switch
    {
        IReadOnlyList<int> ids => "[" + string.Join(",", ids) + "]",
        StatusUpdateMsg s => $"status {s.ObjectId} {string.Join(",", s.AttributeIds)}={string.Join(",", s.Values)}",
        AbnormalStatusUpdateMsg a => $"abnormal {a.ObjectId} " +
                                     string.Join(",", a.Effects.Select(e => $"{e.Id}:{e.Value}:{e.DurationSeconds}")),
        _ => arg?.ToString() ?? "null"
    };

    sealed class StageObserver : ISimulationObserver
    {
        public Dictionary<string, int> Regions { get; } = [];
        public List<string> Completed { get; } = [];
        public List<StageFailure> Failures { get; } = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
            Regions[stage] = regions;
            Completed.Add(stage);
        }

        public void StageFailed(StageFailure failure) => Failures.Add(failure);

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
        {
        }
    }
}
