using Fineage.Protocol;
using Fineage.Zone.Bosses;
using Fineage.Zone.Combat;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Step 9 of <c>docs/architecture/zone-simulation.md</c>: global systems are actors on the scheduler thread. They read
/// the step's time instead of a clock, roll on their own seeded stream, and are out of reach of region bodies.
/// </summary>
public sealed class GlobalActorTests
{
    const int BossNpcId = 25_001;
    const int BossObjectId = 9_700_001;
    static readonly (long Min, long Max) Window = BossCatalog.RaidWindow(1200, 600);

    sealed class ProbeActor() : GlobalActor<long>("probe", ActorTime.Simulation)
    {
        public int Requests;

        public void Request()
        {
            RequireActorThread();
            Requests++;
        }

        protected override void OnStep(long nowUnixMs, List<long> effects) => effects.Add(nowUnixMs);
    }

    sealed class FailureLog : ISimulationObserver
    {
        public List<StageFailure> Failures { get; } = [];

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
        }

        public void StageFailed(StageFailure failure)
        {
            lock (Failures)
                Failures.Add(failure);
        }

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

    public static TheoryData<int> Workers => new() { 0, 4 };

    [Theory]
    [MemberData(nameof(Workers))]
    public void A_region_body_cannot_step_or_ask_an_actor_and_the_failure_names_both(int workers)
    {
        var observer = new FailureLog();
        IRegionExecutor executor = workers == 0 ? SequentialRegionExecutor.Instance : new ParallelRegionExecutor(workers);
        var partition = new GridRegionPartition(1000);
        var scheduler = new RegionTickScheduler(new SimulationClock(100, () => 0), executor, observer);
        var actor = new ProbeActor();
        var stepped = new List<long>();
        scheduler.AddRegionStage(new RegionStage("bodies",
            (_, work) =>
            {
                work.Add(partition.RegionOf(0, 0, 0), 1);
                work.Add(partition.RegionOf(0, 5_000, 0), 2);
            },
            (tick, id) =>
            {
                if (id == 1)
                    actor.Step(tick);
                else
                    actor.Request();
            },
            parallel: true));
        scheduler.AddWorldStage("world", tick =>
        {
            Assert.Null(RegionTickScheduler.CurrentBody);
            stepped.AddRange(actor.Step(tick));
            actor.Request();
        });

        scheduler.Step(100);

        Assert.Equal([100L], stepped);
        Assert.Equal(1, actor.Requests);
        Assert.Equal(2, observer.Failures.Count);
        foreach (var failure in observer.Failures)
        {
            Assert.Equal("bodies", failure.Stage);
            var error = Assert.IsType<InvalidOperationException>(failure.Error);
            Assert.Contains("global actor 'probe'", error.Message);
            Assert.Contains($"region {failure.Scope}", error.Message);
        }

        Assert.Null(RegionTickScheduler.CurrentBody);
    }

    [Fact]
    public void Seven_signs_follows_the_wall_sample_of_the_step_not_the_tick_time()
    {
        var loadedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var behind = new SevenSignsActor(new SevenSignsEngine(loadedAt));
        long boundary = behind.Engine.NextChange.ToUnixTimeMilliseconds();
        long hour = 3_600_000;

        var ahead = new SevenSignsActor(new SevenSignsEngine(loadedAt));
        Assert.Empty(ahead.Step(new TickContext(1, boundary + hour, boundary - 1)));
        Assert.Equal(SevenSignsPeriod.Recruiting, ahead.Engine.Period);

        var change = Assert.Single(behind.Step(new TickContext(1, boundary - hour, boundary)));
        Assert.NotEmpty(change.Notices);
        Assert.Equal(SevenSignsPeriod.Competition, behind.Engine.Period);
    }

    [Fact]
    public void Bosses_follow_the_tick_time_and_measure_the_window_from_the_kill()
    {
        var schedule = new BossSchedule(BossCatalog.From([Boss()]), new MemoryBossStore(), 0);
        var actor = new BossActor(schedule, new SimulationRandom(SimulationRandom.StreamSeed(1, RandomPurpose.Boss)));

        actor.Kill(new BossKill(BossNpcId, 1_000));
        Assert.True(schedule.TryView(BossNpcId, out var dead));
        long delay = dead.RespawnUnixMs - 1_000;
        Assert.InRange(delay, Window.Min, Window.Max);

        long due = dead.RespawnUnixMs;
        Assert.Empty(actor.Step(new TickContext(1, due - 1, due + 3_600_000)));
        Assert.Equal(BossObjectId, Assert.Single(actor.Step(new TickContext(2, due, 0))).ObjectId);
    }

    [Fact]
    public async Task A_boss_killed_in_combat_respawns_a_seeded_window_after_its_death_tick()
    {
        var first = await KillInCombatAsync(seed: 41);
        var again = await KillInCombatAsync(seed: 41);
        var other = await KillInCombatAsync(seed: 42);

        Assert.InRange(first.RespawnUnixMs - first.DeathUnixMs, Window.Min, Window.Max);
        Assert.Equal(first.DeathUnixMs, again.DeathUnixMs);
        Assert.Equal(first.RespawnUnixMs, again.RespawnUnixMs);
        Assert.Equal(first.DeathUnixMs, other.DeathUnixMs);
        Assert.NotEqual(first.RespawnUnixMs, other.RespawnUnixMs);

        var kit = first.Kit;
        Assert.False(kit.World.TryGetEntityById(BossObjectId, out _));
        kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(first.RespawnUnixMs - 10_000);
        kit.World.StepSimulation();
        Assert.False(kit.World.TryGetEntityById(BossObjectId, out _));

        kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(first.RespawnUnixMs + ZoneSimulationSettings.SlowIntervalMilliseconds);
        kit.World.StepSimulation();
        Assert.True(kit.World.TryGetEntityById(BossObjectId, out var boss));
        Assert.Equal(boss.MaxHp, boss.CurHp);
        Assert.False(boss.Dead);
    }

    [Fact]
    public async Task Replaying_a_run_reproduces_the_boss_death_window_and_respawn()
    {
        const ulong seed = 20_261_121;
        var recorded = await KillInCombatAsync(seed, record: true);
        var kit = recorded.Kit;
        kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(recorded.RespawnUnixMs + ZoneSimulationSettings.SlowIntervalMilliseconds);
        kit.World.StepSimulation();
        Assert.True(kit.World.TryGetEntityById(BossObjectId, out _));
        var log = recorded.Log!;
        var (expectedState, expectedFanout) = (Capture(kit, recorded.Schedule), Fanout(recorded.Recorder));

        var (replay, schedule, recorder, _) = await SetupAsync(seed);
        await replay.World.ReplayAsync(log, ms => replay.Now = DateTimeOffset.FromUnixTimeMilliseconds(ms), CancellationToken.None);

        Assert.Equal(expectedState, Capture(replay, schedule));
        Assert.Equal(expectedFanout, Fanout(recorder));
    }

    sealed record Kill(
        NetTestKit Kit,
        BossSchedule Schedule,
        NetBridgeRecorder Recorder,
        SimulationLog? Log,
        long DeathUnixMs,
        long RespawnUnixMs);

    /// <summary>A fighter melees the boss until it dies; the death tick time is the step's.</summary>
    static async Task<Kill> KillInCombatAsync(ulong seed, bool record = false)
    {
        var (kit, schedule, recorder, session) = await SetupAsync(seed);
        var log = record ? kit.World.RecordSimulation() : null;
        var reply = kit.World.ExecuteAsync(session, new AttackCommand(BossObjectId, false), CancellationToken.None);

        long death = -1;
        for (int i = 0; i < 100 && death < 0; i++)
        {
            kit.Now = kit.Now.AddMilliseconds(ZoneSimulationSettings.StepMilliseconds);
            kit.World.StepSimulation();
            if (!kit.World.TryGetEntityById(BossObjectId, out var boss) || boss.Dead)
                death = kit.World.SimulationTicks.Tick * ZoneSimulationSettings.StepMilliseconds;
        }

        Assert.Null(await reply);
        Assert.True(death > 0, "the boss did not die");
        Assert.True(schedule.TryView(BossNpcId, out var view));
        Assert.Equal(BossLifeStatus.Dead, view.Status);
        return new Kill(kit, schedule, recorder, log, death, view.RespawnUnixMs);
    }

    static async Task<(NetTestKit Kit, BossSchedule Schedule, NetBridgeRecorder Recorder, ZoneSession Session)> SetupAsync(
        ulong seed)
    {
        var schedule = new BossSchedule(BossCatalog.From([Boss()]), new MemoryBossStore(), 1_800_000_000_000);
        var kit = new NetTestKit(Map(), "actors", regions: GridRegionPartition.MapTiles, simulationSeed: seed, bosses: schedule);
        var (session, _) = await kit.EnterAsync(level: 45);
        Assert.True(kit.World.TryGetEntityById(BossObjectId, out var boss));
        Assert.True(boss.PDef > 0, "raid boss pawn needs template PDef from the shared materialize path");
        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        lock (recorder.Calls)
            recorder.Calls.Clear();
        return (kit, schedule, recorder, session);
    }

    static List<string> Capture(NetTestKit kit, BossSchedule schedule)
    {
        Assert.True(schedule.TryView(BossNpcId, out var view));
        var state = new List<string> { $"boss {view.Status} respawn={view.RespawnUnixMs} hp={view.CurrentHp}" };
        state.Add(kit.World.TryGetEntityById(BossObjectId, out var e) ? $"pawn hp={e.CurHp} x={e.X} y={e.Y}" : "pawn gone");
        return state;
    }

    static List<string> Fanout(NetBridgeRecorder recorder)
    {
        lock (recorder.Calls)
            return recorder.Calls.Select(c => c.Method + "(" + string.Join(" | ", c.Args.Select(Describe)) + ")").ToList();
    }

    static string Describe(object? arg) => arg switch
    {
        IReadOnlyList<int> ids => "[" + string.Join(",", ids) + "]",
        StatusUpdateMsg s => $"status {s.ObjectId} {string.Join(",", s.AttributeIds)}={string.Join(",", s.Values)}",
        _ => arg?.ToString() ?? "null"
    };

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { ["actors"] = new("actors", true, []) });

    static BossDefinition Boss() =>
        new(BossNpcId, BossKind.Raid, true, BossObjectId, "Greyclaw Kutus", "Raid Boss", "RaidBoss",
            NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 1, 232, 170, 50, Window.Min, Window.Max,
            BossLifeStatus.Alive);
}
