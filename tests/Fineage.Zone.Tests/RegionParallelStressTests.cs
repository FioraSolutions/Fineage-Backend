using System.Collections.Concurrent;
using System.Text.Json;
using Fineage.Zone.Net;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The workers=4 mass-stuck-clients gate (docs/perf/results-2026-11.md, P1): a deterministic
/// sequential-vs-parallel equality run at soak scale with enter/move/attack/leave churn between steps, and a
/// production-shape stress run — the real simulation loop, the parallel executor and concurrent handler-shaped
/// traffic — that must finish every step, never fail a stage and keep the region directory and watcher index
/// consistent. A hung step is reported by the step watchdog naming the stage and region bodies in flight.
/// </summary>
[Trait("Category", "Slow")]
public sealed class RegionParallelStressTests
{
    const int FirstMobId = 9_830_001;
    const int MobCount = 180;
    const int Bleed = 96;

    /// <summary>400-unit regions around Elven Village: the mob/player grid spans a few dozen of them.</summary>
    static readonly GridRegionPartition Fine = new(400, handoffMargin: 40);

    [Fact]
    public async Task Parallel_and_sequential_runs_are_identical_under_soak_scale_churn()
    {
        var sequential = await RunEqualityAsync(SequentialRegionExecutor.Instance);
        var parallel = await RunEqualityAsync(new ParallelRegionExecutor(4));

        Assert.Empty(sequential.Failures);
        Assert.Empty(parallel.Failures);
        Assert.True(parallel.MaxParallelRegions > 8, $"parallel stages ran in only {parallel.MaxParallelRegions} regions");
        Assert.Equal(sequential.State, parallel.State);
        Assert.Equal(sequential.Fanout, parallel.Fanout);
    }

    [Fact]
    public async Task Driven_world_under_parallel_executor_survives_concurrent_handler_traffic()
    {
        var observer = new RecordingObserver();
        var kit = new NetTestKit(Map(), "stress", regions: Fine, simulationObserver: observer,
            simulationSeed: 97, regionExecutor: new ParallelRegionExecutor(4),
            clock: () => DateTimeOffset.UtcNow);
        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;

        // Players and spares are created and entered before the world is driven, while touches are still direct.
        const int initialPlayers = 12;
        const int spareCharacters = 24;
        var sessions = new ConcurrentDictionary<int, ZoneSession>();
        var players = new List<ZoneEntity>();
        for (int i = 0; i < initialPlayers; i++)
        {
            var (session, player) = await kit.EnterAsync(x: NetTestKit.ElfX - 1200 + i * 200,
                y: NetTestKit.ElfY + (i % 3) * 300);
            sessions[player.ObjectId] = session;
            players.Add(player);
            player.CurHp = player.MaxHp / 2;
            kit.World.SyncNeedRegen(player);
        }

        var spares = new ConcurrentQueue<NetTestKit.Character>(
            Enumerable.Range(0, spareCharacters).Select(i => kit.Create(x: NetTestKit.ElfX - 1000 + i * 120,
                y: NetTestKit.ElfY + 1500 + (i % 2) * 250)));
        var left = new ConcurrentQueue<ZoneSession>();

        var bleed = kit.Game.Skills.Find(Bleed) ?? throw new InvalidOperationException($"skill {Bleed} missing");
        Assert.True(bleed.TryLevelData(1, out var bleedLevel));
        foreach (int id in Enumerable.Range(FirstMobId, MobCount))
        {
            Assert.True(kit.World.TryGetEntityById(id, out var mob));
            mob.CurHp = mob.MaxHp / 2;
            kit.World.SyncNeedRegen(mob);
            if (id % 3 != 0)
                Assert.True(kit.World.ApplyAbnormalFromSkillForTest(bleed, bleedLevel, mob, force: true, attacker: players[0]));
        }

        kit.World.DriveForTest();
        using var shutdown = new CancellationTokenSource();
        kit.World.StartSimulation(shutdown.Token);

        var hammerErrors = new ConcurrentQueue<Exception>();
        var watchdogThreshold = TimeSpan.FromSeconds(10);
        var watchdog = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                await Task.Delay(500);
                if (kit.World.StepWatchdogReport(watchdogThreshold) is { } report)
                {
                    hammerErrors.Enqueue(new InvalidOperationException($"step watchdog: {report}"));
                    shutdown.Cancel();
                    return;
                }
            }
        });

        var rng = new Random(7);
        var replyTimeout = TimeSpan.FromSeconds(30);
        async Task HammerAsync(int worker)
        {
            var local = new Random(rng.Next());
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    int roll = local.Next(100);
                    if (roll < 55 && sessions.Values.Count > 0)
                    {
                        var session = sessions.Values.ElementAt(local.Next(sessions.Values.Count));
                        ZoneCommand command = (roll % 3) switch
                        {
                            0 => new MoveCommand(NetTestKit.ElfX - 1500 + local.Next(3200),
                                NetTestKit.ElfY - 300 + local.Next(2400), NetTestKit.ElfZ),
                            1 => new AttackCommand(FirstMobId + local.Next(MobCount), false),
                            _ => new SelectTargetCommand(FirstMobId + local.Next(MobCount))
                        };
                        _ = await kit.World.ExecuteAsync(session, command, CancellationToken.None)
                            .WaitAsync(replyTimeout);
                    }
                    else if (roll < 75 && spares.TryDequeue(out var spare))
                    {
                        var session = new ZoneSession();
                        session.Bind(spare.CharacterId, spare.Account, spare.Name, spare.ClassId);
                        if (await kit.World.TryEnterAsync(session, CancellationToken.None).WaitAsync(replyTimeout))
                            sessions[spare.CharacterId] = session;
                    }
                    else if (roll < 90 && sessions.Count > 4)
                    {
                        var pick = sessions.Keys.ElementAt(local.Next(sessions.Count));
                        if (sessions.TryRemove(pick, out var session))
                        {
                            await kit.World.LeaveAsync(session, CancellationToken.None).WaitAsync(replyTimeout);
                            left.Enqueue(session);
                        }
                    }
                    else
                    {
                        await Task.Delay(20);
                    }
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
            catch (Exception) when (shutdown.IsCancellationRequested)
            {
                // In-flight commands fault with "the zone simulation stopped" once cancellation stops
                // the world; that is the test ending, not a failure.
            }
            catch (Exception ex)
            {
                hammerErrors.Enqueue(ex);
                shutdown.Cancel();
            }
        }

        var hammers = Enumerable.Range(0, 4).Select(HammerAsync).ToArray();
        await Task.Delay(TimeSpan.FromSeconds(25));
        shutdown.Cancel();
        var background = await Task.WhenAll(hammers.Append(watchdog).Select(async t =>
        {
            try
            {
                await t;
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }));

        foreach (var error in background.OfType<Exception>())
            hammerErrors.Enqueue(error);
        Assert.True(hammerErrors.IsEmpty,
            string.Join(" || ", hammerErrors.Take(5).Select(e => e.ToString())));
        Assert.Empty(observer.Failures);
        kit.World.VerifyRegions();
        kit.World.AssertWatcherIndexConsistent();
        Assert.True(recorder.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)).Count > 0,
            "the stress run fanned no vitals out: it did not exercise the regen/abnormal path");
    }

    [Fact]
    public async Task Step_watchdog_names_the_stage_and_region_of_a_body_that_never_returns()
    {
        var release = new ManualResetEventSlim(false);
        var blocker = new BlockingStage(release);
        var observer = new RecordingObserver();
        var kit = new NetTestKit(Map(), "stress", regions: Fine, simulationObserver: observer,
            simulationSeed: 98, regionExecutor: new ParallelRegionExecutor(4), clock: () => DateTimeOffset.UtcNow);
        kit.World.DriveForTest();
        // A due region stage whose first region body never returns: the step it joins can never finish.
        kit.World.SimulationTicks.AddRegionStage(blocker);
        using var shutdown = new CancellationTokenSource();
        kit.World.StartSimulation(shutdown.Token);
        try
        {
            string? report = null;
            for (int i = 0; i < 100 && report == null; i++)
            {
                await Task.Delay(100);
                report = kit.World.StepWatchdogReport(TimeSpan.FromMilliseconds(500));
            }

            Assert.False(string.IsNullOrEmpty(report), "the watchdog did not report the stuck step");
            Assert.Contains(BlockingStage.StageName, report);
            Assert.Contains("has run", report);
        }
        finally
        {
            release.Set();
            shutdown.Cancel();
        }
    }

    sealed class BlockingStage(ManualResetEventSlim release) : IRegionStage
    {
        public const string StageName = "test.blocker";
        public string Name => StageName;
        public bool Parallel => true;

        public void Collect(TickContext tick, RegionWorkCollector work) =>
            work.Add(new RegionId(0, 21, 19), 1);

        public void RunRegion(TickContext tick, RegionId region, IReadOnlyList<int> objectIds) =>
            release.Wait();
    }

    sealed record EqualityRun(List<string> State, List<string> Fanout, List<StageFailure> Failures, int MaxParallelRegions);

    static async Task<EqualityRun> RunEqualityAsync(IRegionExecutor executor)
    {
        var observer = new RecordingObserver();
        var kit = new NetTestKit(Map(), "stress", regions: Fine, simulationObserver: observer,
            simulationSeed: 96, regionExecutor: executor);
        var players = new List<(ZoneSession Session, ZoneEntity Entity)>();
        for (int i = 0; i < 24; i++)
        {
            var entered = await kit.EnterAsync(x: NetTestKit.ElfX - 1500 + (i % 8) * 400,
                y: NetTestKit.ElfY - 200 + (i / 8) * 600);
            players.Add(entered);
        }

        kit.World.JoinPartyForTest(players[0].Entity, players[1].Entity);
        kit.World.JoinPartyForTest(players[2].Entity, players[3].Entity);

        var bleed = kit.Game.Skills.Find(Bleed) ?? throw new InvalidOperationException($"skill {Bleed} missing");
        Assert.True(bleed.TryLevelData(1, out var bleedLevel));
        foreach (int id in Enumerable.Range(FirstMobId, MobCount))
        {
            Assert.True(kit.World.TryGetEntityById(id, out var mob));
            mob.CurHp = mob.MaxHp / 2;
            kit.World.SyncNeedRegen(mob);
            if (id % 3 != 0)
                Assert.True(kit.World.ApplyAbnormalFromSkillForTest(bleed, bleedLevel, mob, force: true,
                    attacker: players[0].Entity));
        }

        foreach (var (_, entity) in players)
        {
            entity.CurHp = entity.MaxHp / 2;
            entity.CurMp = entity.MaxMp / 3;
            kit.World.SyncNeedRegen(entity);
        }

        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(bleed, bleedLevel, players[1].Entity, force: true,
            attacker: players[2].Entity));

        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        lock (recorder.Calls)
            recorder.Calls.Clear();

        var churn = new Random(11);
        int enteredCount = players.Count;
        for (int step = 0; step < 150; step++)
        {
            var replies = new List<Task<string?>>();
            for (int c = 0; c < 6; c++)
            {
                var session = players[churn.Next(enteredCount)].Session;
                ZoneCommand command = churn.Next(3) switch
                {
                    0 => new MoveCommand(NetTestKit.ElfX - 1600 + churn.Next(3400),
                        NetTestKit.ElfY - 400 + churn.Next(2600), NetTestKit.ElfZ),
                    1 => new AttackCommand(FirstMobId + churn.Next(MobCount), false),
                    _ => new SelectTargetCommand(FirstMobId + churn.Next(MobCount))
                };
                replies.Add(kit.World.ExecuteAsync(session, command, CancellationToken.None));
            }

            if (step % 25 == 10 && enteredCount < 40)
            {
                players.Add(await kit.EnterAsync(x: NetTestKit.ElfX - 1400 + enteredCount * 90,
                    y: NetTestKit.ElfY + 1800));
                enteredCount++;
            }

            if (step == 100)
                await kit.World.LeaveAsync(players[5].Session, CancellationToken.None);

            kit.Now = kit.Now.AddMilliseconds(step % 9 == 0 ? 450 : 200);
            kit.World.StepSimulation();
            foreach (var reply in replies)
                _ = await reply;
        }

        var tracked = new List<int>(Enumerable.Range(FirstMobId, MobCount));
        tracked.AddRange(players.Select(p => p.Entity.ObjectId));
        return new EqualityRun(CaptureState(kit, tracked), CaptureFanout(recorder), observer.Failures,
            observer.MaxRegions.GetValueOrDefault("abnormals"));
    }

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["stress"] = new("stress", true,
                Enumerable.Range(0, MobCount).Select(i =>
                    new SpawnNpcDefinition(FirstMobId + i, 98300 + i % 60, "Stressling",
                        NetTestKit.ElfX - 1600 + (i % 15) * 220,
                        NetTestKit.ElfY - 400 + (i / 15) * 210, NetTestKit.ElfZ, 0, true, 300, 1f, 1f, 60, 0, "",
                        "Monster", Level: 1 + i % 7, HpRegen: 2.5 + i % 5, MpRegen: 1.2, MaxMp: 100, AttackRange: 40,
                        AttackSpeed: 253, Aggressive: i % 5 == 1, AggroRange: i % 5 == 1 ? 350 : 0, ClanHelpRange: 0,
                        Clans: []))
                    .ToList())
        });

    static List<string> CaptureState(NetTestKit kit, List<int> tracked)
    {
        var state = new List<string>();
        foreach (int id in tracked.Order())
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
}
