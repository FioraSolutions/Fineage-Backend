using System.Text.Json;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Olympiad;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Step 8 of <c>docs/architecture/zone-simulation.md</c>: region stages may spread their regions over worker threads
/// (<c>FINEAGE_ZONE_REGION_WORKERS</c>), and these tests gate that. The parallel executor must reproduce the
/// sequential run and a recorded command log exactly; aggression sees players across a region edge through the
/// border snapshots, one step late.
/// </summary>
public sealed class RegionParallelWorldTests
{
    const int FirstMobId = 9_810_001;
    const int MobCount = 12;
    const int Bleed = 96;
    const int Relax = 226;
    const int FakeDeath = 60;
    const int Duelist = 88;
    const int WindWalk = 1204;

    static readonly DateTimeOffset OlympiadEvening = new(2026, 10, 28, 18, 30, 0, TimeSpan.Zero);

    /// <summary>400-unit regions around Elven Village, so a few thousand units hold a dozen regions.</summary>
    static readonly GridRegionPartition Fine = new(400, handoffMargin: 40);

    /// <summary>The west edge of tile 22_19, east of Elven Village (21_19).</summary>
    const int TileEdgeX = (22 - GridRegionPartition.MapTileOffsetX) * GridRegionPartition.MapTileSize;

    [Fact]
    public async Task Parallel_regen_does_not_mutate_gear_skills_across_regions()
    {
        const int RingOfQueenAnt = 6660;
        const int RingSkill = 3562;
        DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var bench = new WorldBench(clock: () => now, regionExecutor: new ParallelRegionExecutor(4), simulationSeed: 91);
        var near = await bench.EnterAsync("near", "Near", [new StoredItem(RingOfQueenAnt, 1, -1)], level: 80);
        Assert.Null(await near.Use(near.Slot(RingOfQueenAnt)));
        var far = await bench.EnterAsync("far", "Far", level: 80, x: NetTestKit.ElfX + 2 * GridRegionPartition.MapTileSize);
        Assert.NotEqual(
            bench.World.RegionOwnerOf(near.ObjectId).Region,
            bench.World.RegionOwnerOf(far.ObjectId).Region);
        Assert.Contains(RingSkill, near.Player.GearSkills.Keys);

        var nearGear = new Dictionary<int, int>(near.Player.GearSkills);
        var farGear = new Dictionary<int, int>(far.Player.GearSkills);
        for (int step = 0; step < 30; step++)
        {
            now = now.AddMilliseconds(ZoneSimulationSettings.StepMilliseconds);
            bench.World.StepSimulation();
        }

        Assert.Equal(nearGear, near.Player.GearSkills);
        Assert.Equal(farGear, far.Player.GearSkills);
    }

    [Fact]
    public async Task Parallel_regions_reproduce_the_sequential_run()
    {
        var sequential = await RunAsync(SequentialRegionExecutor.Instance, seed: 81);
        var parallel = await RunAsync(new ParallelRegionExecutor(4), seed: 81);

        Assert.Empty(sequential.Failures);
        Assert.Empty(parallel.Failures);
        Assert.True(parallel.MaxRegions["abnormals"] > 4, $"abnormals ran in {parallel.MaxRegions["abnormals"]} regions");
        Assert.True(parallel.MaxRegions["regen.npcs"] > 4, $"regen.npcs ran in {parallel.MaxRegions["regen.npcs"]} regions");
        Assert.Contains(sequential.Fanout, call => call.StartsWith(nameof(IWorldEventPublisher.FanoutPartyMemberUpdate), StringComparison.Ordinal));
        Assert.Contains(sequential.Fanout, call => call.StartsWith(nameof(IWorldEventPublisher.FanoutAbnormalStatusUpdate), StringComparison.Ordinal));
        Assert.Equal(sequential.State, parallel.State);
        Assert.Equal(sequential.Fanout, parallel.Fanout);
    }

    [Fact]
    public async Task A_command_log_recorded_sequentially_replays_with_parallel_regions()
    {
        var (kit, session, recorder, tracked, observer) = await SetupAsync(SequentialRegionExecutor.Instance, seed: 82);
        var log = kit.World.RecordSimulation();
        for (int step = 0; step < 90; step++)
        {
            var replies = new List<Task<string?>>();
            if (step == 3)
            {
                replies.Add(kit.World.ExecuteAsync(session, new SelectTargetCommand(FirstMobId + 1), CancellationToken.None));
                replies.Add(kit.World.ExecuteAsync(session, new AttackCommand(FirstMobId + 1, false), CancellationToken.None));
            }
            if (step == 40)
                replies.Add(kit.World.ExecuteAsync(session, new MoveCommand(NetTestKit.ElfX - 300, NetTestKit.ElfY + 80, NetTestKit.ElfZ),
                    CancellationToken.None));

            kit.Now = kit.Now.AddMilliseconds(step % 7 == 0 ? 330 : 200);
            kit.World.StepSimulation();
            await Task.WhenAll(replies);
        }

        var recordedState = CaptureState(kit, tracked);
        var recordedFanout = CaptureFanout(recorder);
        Assert.Empty(observer.Failures);

        var (replay, _, replayRecorder, _, replayObserver) = await SetupAsync(new ParallelRegionExecutor(4), seed: 82);
        await replay.World.ReplayAsync(log, ms => replay.Now = DateTimeOffset.FromUnixTimeMilliseconds(ms), CancellationToken.None);

        Assert.Empty(replayObserver.Failures);
        Assert.Equal(recordedState, CaptureState(replay, tracked));
        Assert.Equal(recordedFanout, CaptureFanout(replayRecorder));
    }

    [Fact]
    public async Task Aggression_sees_a_player_across_the_region_edge_through_the_previous_steps_snapshot()
    {
        const int Watcher = 9_820_001;
        const int Local = 9_820_002;
        var map = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["border"] = new("border", true,
            [
                AggressiveMob(Watcher, TileEdgeX - 150, NetTestKit.ElfY),
                AggressiveMob(Local, TileEdgeX + 900, NetTestKit.ElfY + 2000)
            ])
        });
        var observer = new RecordingObserver();
        var kit = new NetTestKit(map, "border", regions: GridRegionPartition.MapTiles, simulationObserver: observer);
        var (_, across) = await kit.EnterAsync(x: TileEdgeX + 400);
        var (_, neighbour) = await kit.EnterAsync(x: TileEdgeX + 900, y: NetTestKit.ElfY + 2200);
        kit.World.NetBridge = NetBridgeRecorder.Create().Item1;
        Assert.True(kit.World.TryGetEntityById(Watcher, out var watcher));
        Assert.True(kit.World.TryGetEntityById(Local, out var local));
        foreach (var mob in new[] { watcher, local })
            mob.GlobalAggro = 0;
        Assert.Equal(new RegionId(0, 21, 19), kit.World.RegionOwnerOf(Watcher).Region);
        Assert.Equal(new RegionId(0, 22, 19), kit.World.RegionOwnerOf(across.ObjectId).Region);
        Assert.Equal(new RegionId(0, 22, 19), kit.World.RegionOwnerOf(Local).Region);

        kit.Now = kit.Now.AddMilliseconds(200);
        kit.World.StepSimulation();

        Assert.Equal(0, watcher.AggroTargetId);
        Assert.Equal(neighbour.ObjectId, local.AggroTargetId);
        var published = kit.World.BorderSnapshotsForTests.Of(new RegionId(0, 22, 19));
        Assert.Contains(published, p => p.ObjectId == across.ObjectId && p.AggroTarget && p.X == across.X);

        watcher.NextAggroScanMs = 0;
        kit.Now = kit.Now.AddMilliseconds(200);
        kit.World.StepSimulation();

        Assert.Equal(across.ObjectId, watcher.AggroTargetId);
        Assert.Equal(1, watcher.HateList[across.ObjectId]);
        Assert.Empty(observer.Failures);
    }

    [Fact]
    public void Every_aggro_range_in_the_data_fits_inside_the_border_snapshot_reach()
    {
        int widest = SpawnCatalog.LoadDefault().Maps.Values.SelectMany(m => m.Npcs).Max(n => n.AggroRange);

        Assert.InRange(widest, 1, ZoneWorld.BorderReach - GridRegionPartition.MapTileHandoffMargin);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(" ", 0)]
    [InlineData("0", 0)]
    [InlineData("4", 4)]
    [InlineData(" 16 ", 16)]
    public void Region_workers_default_to_sequential(string? text, int workers)
    {
        Assert.Equal(workers, ZoneSimulationSettings.ParseRegionWorkers(text));
        Assert.Equal(workers == 0 ? "sequential" : $"parallel:{workers}", ZoneSimulationSettings.DescribeRegionExecutor(workers));
        Assert.IsType(workers == 0 ? typeof(SequentialRegionExecutor) : typeof(ParallelRegionExecutor),
            ZoneSimulationSettings.RegionExecutor(workers));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("four")]
    [InlineData("257")]
    public void Invalid_region_workers_name_the_setting(string text)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ZoneSimulationSettings.ParseRegionWorkers(text));
        Assert.Contains($"FINEAGE_ZONE_REGION_WORKERS='{text}'", error.Message);
    }

    [Fact]
    public void NetTestKit_honors_FINEAGE_ZONE_REGION_WORKERS_when_no_executor_is_passed()
    {
        string? prior = Environment.GetEnvironmentVariable("FINEAGE_ZONE_REGION_WORKERS");
        try
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_REGION_WORKERS", "4");
            var kit = new NetTestKit(simulationSeed: 1);
            Assert.IsType<ParallelRegionExecutor>(kit.World.SimulationTicks.Executor);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FINEAGE_ZONE_REGION_WORKERS", prior);
        }
    }

    [Fact]
    public async Task Toggle_and_fake_death_in_different_regions_reproduce_the_sequential_run()
    {
        var sequential = await RunToggleAndFakeDeathAsync(SequentialRegionExecutor.Instance);
        var parallel = await RunToggleAndFakeDeathAsync(new ParallelRegionExecutor(4));

        Assert.Empty(sequential.Failures);
        Assert.Empty(parallel.Failures);
        Assert.Equal(sequential.State, parallel.State);
        Assert.Equal(sequential.Fanout, parallel.Fanout);
    }

    [Fact]
    public async Task Duels_reproduce_the_sequential_run_under_parallel_regions()
    {
        var sequential = await RunDuelAsync(SequentialRegionExecutor.Instance);
        var parallel = await RunDuelAsync(new ParallelRegionExecutor(4));

        Assert.Empty(sequential.Failures);
        Assert.Empty(parallel.Failures);
        Assert.Equal(sequential.State, parallel.State);
        Assert.Equal(sequential.Fanout, parallel.Fanout);
    }

    [Fact]
    public async Task Olympiad_arena_abnormals_reproduce_the_sequential_run_under_parallel_regions()
    {
        var sequential = await RunOlympiadAsync(SequentialRegionExecutor.Instance);
        var parallel = await RunOlympiadAsync(new ParallelRegionExecutor(4));

        Assert.Empty(sequential.Failures);
        Assert.Empty(parallel.Failures);
        Assert.Equal(sequential.State, parallel.State);
        Assert.Equal(sequential.Fanout, parallel.Fanout);
    }

    [Fact]
    public void A_parallel_executor_refuses_an_injected_random_shared_by_every_region()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new NetTestKit(combatRandom: new Random(1), regionExecutor: new ParallelRegionExecutor(2)));
        Assert.Contains("injected random generators (Combat)", error.Message);

        _ = new NetTestKit(combatRandom: new Random(1), regionExecutor: SequentialRegionExecutor.Instance);
    }

    sealed record Run(List<string> State, List<string> Fanout, List<StageFailure> Failures, Dictionary<string, int> MaxRegions);

    static async Task<Run> RunToggleAndFakeDeathAsync(IRegionExecutor executor)
    {
        var observer = new RecordingObserver();
        var kit = new NetTestKit(regions: GridRegionPartition.MapTiles, regionExecutor: executor, simulationObserver: observer,
            simulationSeed: 93);
        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (relaxSession, relaxPlayer) = await kit.EnterAsync(classId: 7, level: 40);
        var (fakeSession, fakePlayer) = await kit.EnterAsync(classId: 7, level: 40,
            x: NetTestKit.ElfX + 2 * GridRegionPartition.MapTileSize);
        Assert.NotEqual(
            GridRegionPartition.MapTiles.RegionOf(0, relaxPlayer.X, relaxPlayer.Y),
            GridRegionPartition.MapTiles.RegionOf(0, fakePlayer.X, fakePlayer.Y));

        relaxPlayer.KnownSkills[Relax] = 1;
        relaxPlayer.Sitting = true;
        relaxPlayer.CurMp = 300;
        fakePlayer.KnownSkills[FakeDeath] = 1;
        fakePlayer.CurMp = 400;
        Assert.Null(await kit.World.TrySkillUseAsync(relaxSession, Relax, 0, CancellationToken.None));
        Assert.Null(await kit.World.TrySkillUseAsync(fakeSession, FakeDeath, 0, CancellationToken.None));

        for (int step = 0; step < 20; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(step % 5 == 0 ? 666 : 200);
            kit.World.StepSimulation();
        }

        return new Run(
            [DescribeTogglePlayer(relaxPlayer), DescribeTogglePlayer(fakePlayer)],
            CaptureFanout(recorder),
            observer.Failures,
            observer.MaxRegions);
    }

    static async Task<Run> RunDuelAsync(IRegionExecutor executor)
    {
        var observer = new RecordingObserver();
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var bench = new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty, regionExecutor: executor,
            simulationObserver: observer, simulationSeed: 94);
        const int duelX = 46045, duelY = 41251, duelZ = -3440;
        var a = await bench.EnterAsync("parallel_duel_a", "Alpha", [new StoredItem(57, 1000, -1)], level: 60, x: duelX,
            y: duelY, z: duelZ, clearRecorder: false);
        var b = await bench.EnterAsync("parallel_duel_b", "Bravo", [new StoredItem(57, 1000, -1)], level: 60,
            x: duelX + 100, y: duelY, z: duelZ, clearRecorder: false);
        a.Player.CurHp = a.Player.MaxHp = 3000;
        b.Player.CurHp = b.Player.MaxHp = 3000;

        Assert.Null(await bench.World.TryDuelCommandAsync(a.Session,
            new DuelCommandNetMsg { Kind = DuelCommandKind.Request, TargetName = b.Player.Name }, CancellationToken.None));
        Assert.Null(await bench.World.TryDuelCommandAsync(b.Session,
            new DuelCommandNetMsg { Kind = DuelCommandKind.Answer, Response = 1 }, CancellationToken.None));
        for (int i = 0; i < 8; i++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            bench.World.RunDuelTickForTest();
            bench.World.StepSimulation();
        }

        Assert.Equal(DuelFighterState.Duelling, a.Player.DuelState);
        bench.World.SkillDamagePlayerForTest(a.Player, b.Player, 1200);

        for (int step = 0; step < 6; step++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            bench.World.RunDuelTickForTest();
            bench.World.StepSimulation();
        }

        lock (bench.Rec.Calls)
            bench.Rec.Calls.Clear();
        bench.World.StepSimulation();

        return new Run(
            [DescribeDuelPlayer(a.Player), DescribeDuelPlayer(b.Player)],
            CaptureFanout(bench.Rec),
            observer.Failures,
            observer.MaxRegions);
    }

    static async Task<Run> RunOlympiadAsync(IRegionExecutor executor)
    {
        var observer = new RecordingObserver();
        var clock = OlympiadEvening;
        var bench = new WorldBench(clock: () => clock, olympiadSettings: new OlympiadSettings { ClassedMin = 2, WaitSeconds = 5 },
            regionExecutor: executor, simulationObserver: observer, simulationSeed: 95);
        var one = await bench.EnterAsync("parallel_oly_1", "One", classId: Duelist, level: 78, clearRecorder: false);
        var two = await bench.EnterAsync("parallel_oly_2", "Two", classId: Duelist, level: 78, clearRecorder: false);
        bench.World.TrySetNoble(one.ObjectId, true);
        bench.World.TrySetNoble(two.ObjectId, true);
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(one.Session, new OlympiadRegisterRequestMsg { Classed = 1 },
            CancellationToken.None));
        Assert.Null(await bench.World.TryOlympiadRegisterAsync(two.Session, new OlympiadRegisterRequestMsg { Classed = 1 },
            CancellationToken.None));

        async Task AdvanceAsync(DateTimeOffset when)
        {
            clock = when;
            await bench.World.AdvanceOlympiadAsync(CancellationToken.None);
        }

        await AdvanceAsync(OlympiadEvening.AddSeconds(61));
        await AdvanceAsync(OlympiadEvening.AddSeconds(66));
        Assert.True(bench.World.InOlympiadArena(one.ObjectId));

        var wind = bench.Game.Skills.Find(WindWalk) ?? throw new InvalidOperationException($"skill {WindWalk} missing");
        Assert.True(wind.TryLevelData(1, out var windLevel));
        Assert.True(bench.World.ApplyAbnormalFromSkillForTest(wind, windLevel, one.Player));

        clock = clock.AddSeconds(wind.AbnormalTimeSeconds + 1);
        for (int step = 0; step < 8; step++)
            bench.World.StepSimulation();

        lock (bench.Rec.Calls)
            bench.Rec.Calls.Clear();
        bench.World.StepSimulation();

        return new Run(
            [DescribeOlympiadPlayer(one.Player), DescribeOlympiadPlayer(two.Player)],
            CaptureFanout(bench.Rec),
            observer.Failures,
            observer.MaxRegions);
    }

    static async Task<Run> RunAsync(IRegionExecutor executor, ulong seed)
    {
        var (kit, _, recorder, tracked, observer) = await SetupAsync(executor, seed);
        for (int step = 0; step < 100; step++)
        {
            kit.Now = kit.Now.AddMilliseconds(step % 9 == 0 ? 450 : 200);
            kit.World.StepSimulation();
        }

        return new Run(CaptureState(kit, tracked), CaptureFanout(recorder), observer.Failures, observer.MaxRegions);
    }

    /// <summary>
    /// Wounded, bleeding mobs and players spread over a dozen fine regions, two players in a party, a few aggressive
    /// mobs ready to scan: every region stage and the barrier bookkeeping have work in many regions at once.
    /// </summary>
    static async Task<(NetTestKit Kit, ZoneSession Session, NetBridgeRecorder Recorder, int[] Tracked, RecordingObserver Observer)>
        SetupAsync(IRegionExecutor executor, ulong seed)
    {
        var observer = new RecordingObserver();
        var kit = new NetTestKit(Map(), "parallel", regions: Fine, simulationObserver: observer, simulationSeed: seed,
            regionExecutor: executor);
        var (session, first) = await kit.EnterAsync();
        var (_, second) = await kit.EnterAsync(x: NetTestKit.ElfX + 700);
        var (_, third) = await kit.EnterAsync(x: NetTestKit.ElfX - 1300, y: NetTestKit.ElfY + 500);
        kit.World.JoinPartyForTest(first, second);
        var bleed = kit.Game.Skills.Find(Bleed) ?? throw new InvalidOperationException($"skill {Bleed} missing");
        Assert.True(bleed.TryLevelData(1, out var bleedLevel));

        int[] mobs = [.. Enumerable.Range(FirstMobId, MobCount)];
        foreach (int id in mobs)
        {
            Assert.True(kit.World.TryGetEntityById(id, out var mob));
            if (mob.Aggressive)
                mob.GlobalAggro = 0;
            mob.CurHp = mob.MaxHp / 2;
            kit.World.SyncNeedRegen(mob);
            if (id % 3 != 0)
                Assert.True(kit.World.ApplyAbnormalFromSkillForTest(bleed, bleedLevel, mob, force: true, attacker: third));
        }

        foreach (var player in new[] { first, second, third })
        {
            player.CurHp = player.MaxHp / 2;
            player.CurMp = player.MaxMp / 3;
            kit.World.SyncNeedRegen(player);
        }
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(bleed, bleedLevel, second, force: true, attacker: third));

        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        lock (recorder.Calls)
            recorder.Calls.Clear();
        return (kit, session, recorder, [first.ObjectId, second.ObjectId, third.ObjectId, .. mobs], observer);
    }

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["parallel"] = new("parallel", true,
                Enumerable.Range(0, MobCount).Select(i =>
                    new SpawnNpcDefinition(FirstMobId + i, 98100 + i, "Gremlin", NetTestKit.ElfX - 1500 + i * 280,
                        NetTestKit.ElfY + (i % 3) * 420, NetTestKit.ElfZ, 0, true, 300, 1f, 1f, 60, 0, "", "Monster",
                        Level: 1 + i % 5, HpRegen: 2.5 + i, MpRegen: 1.2, MaxMp: 100, AttackRange: 40, AttackSpeed: 253,
                        Aggressive: i % 4 == 1, AggroRange: i % 4 == 1 ? 350 : 0, ClanHelpRange: 0, Clans: []))
                    .ToList())
        });

    static SpawnNpcDefinition AggressiveMob(int objectId, int x, int y) =>
        new(objectId, 98200 + objectId % 100, "Watcher", x, y, NetTestKit.ElfZ, 0, true, 500, 80f, 40f, 60, 0, "", "Monster",
            Level: 5, AttackRange: 40, AttackSpeed: 253, Aggressive: true, AggroRange: 1000, ClanHelpRange: 0, Clans: []);

    static string DescribeTogglePlayer(ZoneEntity e) =>
        $"{e.ObjectId} mp={e.CurMp} sit={e.Sitting} fake={e.FakeDeath} ab=[{string.Join(",", e.Abnormals.Select(a => a.Id))}]";

    static string DescribeDuelPlayer(ZoneEntity e) =>
        $"{e.ObjectId} hp={e.CurHp} duel={e.DuelId}/{e.DuelState} team={e.DuelTeam}";

    static string DescribeOlympiadPlayer(ZoneEntity e) =>
        $"{e.ObjectId} hp={e.CurHp} mp={e.CurMp} ab=[{string.Join(",", e.Abnormals.Select(a => a.Id))}]";

    static List<string> CaptureState(NetTestKit kit, int[] tracked)
    {
        var state = new List<string>();
        foreach (int id in tracked)
        {
            if (!kit.World.TryGetEntityById(id, out var e))
            {
                state.Add($"{id} gone");
                continue;
            }

            string abnormals = string.Join(",", e.Abnormals.Select(a => $"{a.Id}:{a.ExpiresAtUnixMs}:{a.NextDotAtUnixMs}"));
            string hate = string.Join(",", e.HateList.OrderBy(h => h.Key).Select(h => $"{h.Key}={h.Value}"));
            state.Add($"{id} hp={e.CurHp} mp={e.CurMp} cp={e.CurCp} x={e.X} y={e.Y} move={e.MoveActive} dead={e.Dead} " +
                      $"aggro={e.AggroTargetId} hate=[{hate}] carry={e.RegenCarryHp:F6}/{e.RegenCarryMp:F6} ab=[{abnormals}] " +
                      $"sets={e.ActiveSets}");
        }

        return state;
    }

    static List<string> CaptureFanout(NetBridgeRecorder recorder)
    {
        lock (recorder.Calls)
            return recorder.Calls.Select(Describe).ToList();
    }

    static string Describe((string Method, object?[] Args) call) =>
        call.Method + "(" + string.Join(" | ", call.Args.Select(DescribeArg)) + ")";

    static string DescribeArg(object? arg) => arg switch
    {
        IReadOnlyList<int> ids => "[" + string.Join(",", ids) + "]",
        null => "null",
        string or int or long or bool or float or double or byte or short => arg.ToString()!,
        _ => JsonSerializer.Serialize(arg, arg.GetType())
    };

    sealed class RecordingObserver : ISimulationObserver
    {
        public List<StageFailure> Failures { get; } = [];

        public Dictionary<string, int> MaxRegions { get; } = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed) =>
            MaxRegions[stage] = Math.Max(regions, MaxRegions.GetValueOrDefault(stage));

        public void StageFailed(StageFailure failure) => Failures.Add(failure);

        public void TicksSkipped(TickContext tick, long skipped)
        {
        }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers)
        {
        }

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
        {
        }
    }
}
