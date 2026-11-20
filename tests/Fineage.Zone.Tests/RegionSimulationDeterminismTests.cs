using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Combat, regen and abnormal/DoT ticks share one <see cref="RegionTickScheduler"/>. Outcomes must not depend on the
/// region partition, and identical worlds must produce identical state and fanout sequence.
/// </summary>
public sealed class RegionSimulationDeterminismTests
{
    const int FirstMobId = 9_700_001;
    const int MobCount = 6;
    const int MobSpacing = 7_000;
    const int BleedSkillId = 96;
    const int CombatSteps = 20;
    const int RegenSteps = 12;

    static readonly string[] LegacyCombatOrder =
    [
        "movement.player", "arena", "movement.npc", "random-walk", "pending-hits", "melee-hits", "pvp-flags",
        "auto-attacks", "pending-casts", "queued-moves", "trade-range", "party", "pending-pickups", "pending-talks",
        "fear", "aggression", "leash", "mob-attacks", "summons", "cubics", "duels", "corpse-decay", "attack-stance",
        "ground-expiry"
    ];

    static readonly string[] LegacySimulationOrder =
    [
        "regen.players", "regen.npcs", "spawns", "abnormals", "bosses", "clan.dissolutions", "clan.hall-auctions",
        "sieges", "manor", "fishing", "lottery", "seven-signs", "mammon", "olympiad"
    ];

    [Fact]
    public async Task Identical_worlds_produce_identical_state_and_fanout_sequence()
    {
        var first = await RunRegenAsync(GridRegionPartition.MapTiles);
        var second = await RunRegenAsync(GridRegionPartition.MapTiles);

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Fanout, second.Fanout);
        Assert.NotEmpty(first.Fanout);
    }

    [Fact]
    public async Task Regen_and_dot_outcome_does_not_depend_on_the_region_partition()
    {
        var tiles = await RunRegenAsync(GridRegionPartition.MapTiles);
        var fine = await RunRegenAsync(new GridRegionPartition(500));
        var whole = await RunRegenAsync(new GridRegionPartition(1 << 30));

        Assert.Equal(tiles.State, fine.State);
        Assert.Equal(tiles.State, whole.State);
        Assert.Equal(Sorted(tiles.Fanout), Sorted(fine.Fanout));
        Assert.Equal(Sorted(tiles.Fanout), Sorted(whole.Fanout));
    }

    [Fact]
    public async Task Identical_worlds_produce_identical_combat_state_and_fanout_sequence()
    {
        var first = await RunCombatAsync();
        var second = await RunCombatAsync();

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Fanout, second.Fanout);
    }

    [Fact]
    public async Task Regen_and_abnormal_stages_are_spread_across_regions()
    {
        var observer = new StageObserver();
        var (kit, _, _, _) = await SetupAsync(GridRegionPartition.MapTiles, observer);

        await kit.World.RunRegenTickAsync(CancellationToken.None);
        await kit.World.RunAbnormalTickAsync(CancellationToken.None);

        // Elven Village sits on tile 21_19; the eastern mobs and the second player are on 22_19.
        Assert.Equal(2, observer.Regions["regen.players"]);
        Assert.Equal(2, observer.Regions["regen.npcs"]);
        Assert.Equal(2, observer.Regions["abnormals"]);
        Assert.Empty(observer.Failures);
    }

    [Fact]
    public void Combat_stages_run_every_step_and_the_3s_stages_once_per_15_steps_in_legacy_order()
    {
        var observer = new StageObserver();
        var kit = new NetTestKit(Map(), "determinism", regions: GridRegionPartition.MapTiles, simulationObserver: observer);
        Assert.Equal(ZoneCombat.AutoAttackTickMs, kit.World.SimulationTicks.StepMilliseconds);
        Assert.Equal(ZoneRegen.DefaultTickMs / ZoneCombat.AutoAttackTickMs, ZoneSimulationSettings.SlowPeriodTicks);
        Assert.Equal(LegacyCombatOrder.Concat(LegacySimulationOrder), kit.World.SimulationTicks.StageNames);

        var expected = new List<string>();
        for (int step = 1; step <= 2 * ZoneSimulationSettings.SlowPeriodTicks; step++)
        {
            kit.World.StepSimulation();
            expected.AddRange(LegacyCombatOrder);
            if (step % ZoneSimulationSettings.SlowPeriodTicks == 0)
                expected.AddRange(LegacySimulationOrder);
            Assert.Equal(expected, observer.Completed);
        }

        Assert.Empty(observer.Failures);
    }

    [Fact]
    public void Combat_hook_runs_the_combat_cadence_without_advancing_the_clock()
    {
        var observer = new StageObserver();
        var kit = new NetTestKit(Map(), "determinism", regions: GridRegionPartition.MapTiles, simulationObserver: observer);

        kit.World.RunPlayerCombatTickAsync(CancellationToken.None).GetAwaiter().GetResult();

        Assert.Equal(LegacyCombatOrder, observer.Completed);
        Assert.Equal(0, kit.World.SimulationTicks.Tick);
    }

    [Theory]
    [InlineData(3000, 15)]
    [InlineData(200, 1)]
    [InlineData(6000, 30)]
    public void Slow_period_is_the_interval_in_simulation_steps(int intervalMs, int expected) =>
        Assert.Equal(expected, ZoneSimulationSettings.SlowPeriodFor(intervalMs, ZoneCombat.AutoAttackTickMs));

    [Theory]
    [InlineData(2500)]
    [InlineData(100)]
    public void Slow_interval_that_is_not_a_step_multiple_is_rejected_with_the_variable_name(int intervalMs)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ZoneSimulationSettings.SlowPeriodFor(intervalMs, ZoneCombat.AutoAttackTickMs));
        Assert.Contains("FINEAGE_ZONE_TICK_MS", error.Message);
    }

    static async Task<(List<string> State, List<string> Fanout)> RunRegenAsync(IRegionPartition partition)
    {
        var (kit, recorder, tracked, _) = await SetupAsync(partition, new StageObserver());
        for (int step = 0; step < RegenSteps; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(ZoneRegen.DefaultTickMs);
            await kit.World.RunRegenTickAsync(CancellationToken.None);
            await kit.World.RunAbnormalTickAsync(CancellationToken.None);
        }

        return (CaptureState(kit, tracked), CaptureFanout(recorder));
    }

    /// <summary>A player auto-attacks a mob while the other mobs random-walk, on seeded combat and spawn rolls.</summary>
    static async Task<(List<string> State, List<string> Fanout)> RunCombatAsync()
    {
        var (kit, recorder, tracked, attacker) = await SetupAsync(GridRegionPartition.MapTiles, new StageObserver());
        Assert.True(kit.World.TryGetEntityById(FirstMobId, out var target));
        int targetHp = target.CurHp;
        Assert.Null(await kit.World.TryAttackAsync(attacker, FirstMobId, CancellationToken.None));
        for (int step = 0; step < CombatSteps; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(ZoneCombat.AutoAttackTickMs);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        }

        Assert.True(target.CurHp < targetHp, $"auto-attack never hit: hp {target.CurHp} of {targetHp}");

        return (CaptureState(kit, tracked), CaptureFanout(recorder));
    }

    static List<string> CaptureState(NetTestKit kit, int[] tracked)
    {
        var state = new List<string>();
        foreach (int id in tracked)
        {
            Assert.True(kit.World.TryGetEntityById(id, out var e));
            string abnormals = string.Join(",", e.Abnormals.Select(a => $"{a.Id}:{a.ExpiresAtUnixMs}:{a.NextDotAtUnixMs}"));
            state.Add($"{id} hp={e.CurHp} mp={e.CurMp} cp={e.CurCp} x={e.X} y={e.Y} move={e.MoveActive} " +
                      $"carry={e.RegenCarryHp:R}/{e.RegenCarryMp:R}/{e.RegenCarryCp:R} ab=[{abnormals}]");
        }

        return state;
    }

    static List<string> CaptureFanout(NetBridgeRecorder recorder)
    {
        lock (recorder.Calls)
            return recorder.Calls.Select(Describe).ToList();
    }

    static async Task<(NetTestKit Kit, NetBridgeRecorder Recorder, int[] Tracked, ZoneSession West)> SetupAsync(
        IRegionPartition partition, ISimulationObserver observer)
    {
        var kit = new NetTestKit(Map(), "determinism", regions: partition, simulationObserver: observer,
            combatRandom: new Random(7), spawnRandom: new Random(1));
        var (westSession, west) = await kit.EnterAsync();
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
        return (kit, recorder, tracked, westSession);
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
