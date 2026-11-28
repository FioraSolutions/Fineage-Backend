using Fineage.Agent.Contracts;
using Fineage.Agent.Identity;
using Fineage.Agent.Observation;
using Fineage.Zone.Interest;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// AI-004: observation projection through the real Zone knownlist / request path.
/// Visibility is the player knownlist (AOI + instance + non-GM hide), not a second algorithm.
/// </summary>
public sealed class AgentObservationProjectionTests
{
    readonly ITestOutputHelper _output;

    public AgentObservationProjectionTests(ITestOutputHelper output) => _output = output;

    const int NearNpcId = 9_700_000;
    const int FarNpcId = 9_799_999;
    const int HiddenNpcId = 9_700_001;

    [Fact]
    public async Task Snapshot_visible_ids_match_the_player_knownlist()
    {
        var bench = new WorldBench(aoi: new RangeAoi(2_000), spawns: Cluster("observe", 5), mapId: "observe");
        var player = await bench.EnterAsync();
        var snapshot = await bench.World.PublishObservationAsync(player.Session, CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(player.Session.ObjectId, snapshot!.ObjectId);
        Assert.Equal(player.Session.ObjectId, snapshot.Self.ObjectId);
        AssertKnownlistParity(player.Player, snapshot);
    }

    [Fact]
    public async Task Hidden_and_out_of_range_entities_are_not_in_the_payload()
    {
        var catalog = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["observe"] = new("observe", true,
            [
                Npc(NearNpcId, 97_000, "NearNpc", NetTestKit.ElfX + 40, NetTestKit.ElfY, NetTestKit.ElfZ),
                Npc(HiddenNpcId, 97_001, "HiddenNpc", NetTestKit.ElfX + 50, NetTestKit.ElfY, NetTestKit.ElfZ),
                Npc(FarNpcId, 97_002, "FarNpc", NetTestKit.ElfX + 20_000, NetTestKit.ElfY, NetTestKit.ElfZ)
            ])
        });
        var bench = new WorldBench(aoi: new RangeAoi(400), spawns: catalog, mapId: "observe");
        Assert.True(bench.World.TryGetEntityById(HiddenNpcId, out var hidden));
        hidden.Invisible = true;

        var player = await bench.EnterAsync();

        Assert.Contains(NearNpcId, player.Player.Knownlist);
        Assert.DoesNotContain(HiddenNpcId, player.Player.Knownlist);
        Assert.DoesNotContain(FarNpcId, player.Player.Knownlist);

        var snapshot = await bench.World.PublishObservationAsync(player.Session, CancellationToken.None);
        Assert.NotNull(snapshot);
        var ids = snapshot!.Visible.Select(v => v.ObjectId).ToHashSet();
        Assert.DoesNotContain(HiddenNpcId, ids);
        Assert.DoesNotContain(FarNpcId, ids);
        Assert.Contains(NearNpcId, ids);
        AssertKnownlistParity(player.Player, snapshot);
    }

    [Fact]
    public async Task Observe_pages_when_the_knownlist_exceeds_the_entity_cap()
    {
        const int count = 150;
        var bench = new WorldBench(aoi: new RangeAoi(4_000), spawns: Cluster("observe", count), mapId: "observe");
        var player = await bench.EnterAsync();
        var (identity, character, adapter) = BindAgent(bench, player, AgentScope.WorldRead);

        var union = new HashSet<int>();
        int offset = 0;
        int pages = 0;
        int lastCount = 0;
        int totalVisible = 0;
        bool hasMore;
        do
        {
            AgentObservationResult result = pages == 0
                ? await adapter.ObserveFreshAsync(identity, character, bench.World, player.Session)
                : await adapter.ObserveAsync(
                    identity, character, bench.World, player.Session, new AgentObservationQuery(PageOffset: offset));
            Assert.True(result.Ok, result.Detail);
            AgentObservation page = result.Observation!;
            Assert.True(AgentContractJson.MeasureUtf8(page) <= AgentObservation.MaxPayloadBytes);
            Assert.True(page.Entities.Count <= AgentObservation.MaxEntities);
            Assert.Equal(offset, page.PageOffset);
            foreach (var entity in page.Entities)
                Assert.True(union.Add(entity.ObjectId), $"duplicate entity {entity.ObjectId} across pages");
            totalVisible = page.TotalVisible;
            lastCount = page.Entities.Count;
            hasMore = page.HasMore;
            offset += page.Entities.Count;
            pages++;
            Assert.True(pages <= 8, "paging did not terminate");
        } while (hasMore);

        Assert.True(player.Player.Knownlist.Count >= count, $"knownlist was {player.Player.Knownlist.Count}");
        Assert.Equal(player.Player.Knownlist.Count, totalVisible);
        Assert.True(pages >= 2, "a 150-NPC knownlist must page rather than silently truncate");
        Assert.True(lastCount < totalVisible);
        Assert.Equal(player.Player.Knownlist.ToHashSet(), union);
        Assert.DoesNotContain(player.Session.ObjectId, union);
    }

    [Fact]
    public async Task World_read_scope_is_enforced_on_the_real_zone_path()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        var (identity, character, adapter) = BindAgent(bench, player, AgentScope.QuestRead);
        AgentObservationResult denied = await adapter.ObserveFreshAsync(identity, character, bench.World, player.Session);
        Assert.False(denied.Ok);
        Assert.Equal(AgentRefusalCodes.ScopeDenied, denied.RefusalCode);

        var allowedBind = BindAgent(bench, player, AgentScope.WorldRead, agentName: "agent-allowed");
        AgentObservationResult allowed = await allowedBind.Adapter.ObserveFreshAsync(
            allowedBind.Identity, allowedBind.Character, bench.World, player.Session);
        Assert.True(allowed.Ok, allowed.Detail);
        Assert.NotNull(allowed.Observation!.Self);
        Assert.Equal(player.Session.ObjectId, allowed.Observation.Self!.ObjectId);
    }

    [Fact]
    public async Task Published_snapshot_is_dropped_when_the_character_leaves()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        Assert.NotNull(await bench.World.PublishObservationAsync(player.Session, CancellationToken.None));
        Assert.True(bench.World.TryGetPublishedObservation(player.Session.ObjectId, out _));
        await bench.World.LeaveAsync(player.Session, CancellationToken.None);
        Assert.False(bench.World.TryGetPublishedObservation(player.Session.ObjectId, out _));
    }

    [Fact]
    public async Task Town_and_raid_payload_bytes_are_measured_and_bounded()
    {
        var town = await MeasureSceneAsync("town", npcCount: 80);
        var raid = await MeasureSceneAsync("raid", npcCount: 200);
        Assert.True(town.Bytes <= AgentObservation.MaxPayloadBytes, $"town payload {town.Bytes}");
        Assert.True(raid.Bytes <= AgentObservation.MaxPayloadBytes, $"raid payload {raid.Bytes}");
        Assert.True(town.Entities <= AgentObservation.MaxEntities);
        Assert.True(raid.Entities <= AgentObservation.MaxEntities);
        Assert.True(raid.TotalVisible >= 200, $"raid visible {raid.TotalVisible}");
        Assert.True(raid.HasMore, "a 200-entity raid scene must page rather than silently truncate");
        _output.WriteLine($"town payload: visible={town.Visible} page={town.Entities}/{town.TotalVisible} hasMore={town.HasMore} bytes={town.Bytes}");
        _output.WriteLine($"raid payload: visible={raid.Visible} page={raid.Entities}/{raid.TotalVisible} hasMore={raid.HasMore} bytes={raid.Bytes}");
        NoteMetrics($"payload town_visible={town.Visible} town_page={town.Entities} town_bytes={town.Bytes} raid_visible={raid.Visible} raid_page={raid.Entities} raid_bytes={raid.Bytes} raid_hasMore={raid.HasMore}");
    }

    [Fact]
    public async Task Observing_published_snapshots_does_not_lock_the_tick()
    {
        var recorder = new TickRecorder();
        var bench = new WorldBench(aoi: new RangeAoi(1_000), simulationObserver: recorder);
        var player = await bench.EnterAsync();
        Assert.NotNull(await bench.World.PublishObservationAsync(player.Session, CancellationToken.None));

        using var shutdown = new CancellationTokenSource();
        bench.World.StartSimulation(shutdown.Token);
        try
        {
            await WaitForTicksAsync(recorder, 8, TimeSpan.FromSeconds(8));
            recorder.BeginWindow();
            await WaitForTicksAsync(recorder, 12, TimeSpan.FromSeconds(8));
            double baselineP99 = recorder.WindowP99();

            recorder.BeginWindow();
            using var stop = new CancellationTokenSource();
            Task[] workers = StartObservers(bench.World, player.Session.ObjectId, observers: 32, stop.Token);
            await WaitForTicksAsync(recorder, 12, TimeSpan.FromSeconds(8));
            await stop.CancelAsync();
            await Task.WhenAll(workers);
            double observeP99 = recorder.WindowP99();

            _output.WriteLine($"tick lock (Release/CI window): baseline p99={baselineP99:F2} ms, observe p99={observeP99:F2} ms");
            NoteMetrics($"tick-lock baseline_p99_ms={baselineP99:F2} observe_p99_ms={observeP99:F2}");
            Assert.True(baselineP99 < 200, $"baseline tick p99 {baselineP99:F2} ms exceeded the 200 ms step budget");
            Assert.True(observeP99 < 200, $"observe tick p99 {observeP99:F2} ms exceeded the 200 ms step budget");
            // Off-thread snapshot reads must not stall the tick; allow a small measurement jitter.
            Assert.True(observeP99 < baselineP99 + 20, $"observe p99 {observeP99:F2} ms vs baseline {baselineP99:F2} ms");
        }
        finally
        {
            await shutdown.CancelAsync();
            await bench.World.WaitSimulationAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    [Trait("Category", "perf")]
    public async Task Debug_observe_tick_p99_with_many_agents()
    {
        var recorder = new TickRecorder();
        var bench = new WorldBench(
            aoi: new RangeAoi(2_000),
            spawns: Cluster("observe", 80),
            mapId: "observe",
            simulationObserver: recorder);
        var player = await bench.EnterAsync();
        Assert.NotNull(await bench.World.PublishObservationAsync(player.Session, CancellationToken.None));

        using var shutdown = new CancellationTokenSource();
        bench.World.StartSimulation(shutdown.Token);
        try
        {
            await WaitForTicksAsync(recorder, 10, TimeSpan.FromSeconds(10));
            recorder.BeginWindow();
            const int observers = 64;
            using var stop = new CancellationTokenSource();
            Task[] workers = StartObservers(bench.World, player.Session.ObjectId, observers, stop.Token);
            await Task.Delay(TimeSpan.FromSeconds(12));
            await stop.CancelAsync();
            await Task.WhenAll(workers);
            var stats = recorder.WindowStats();
            Assert.True(stats.Count >= 20, $"expected at least 20 ticks, got {stats.Count}");
            _output.WriteLine($"Debug observe tick p99={stats.P99:F2} ms p50={stats.P50:F2} max={stats.Max:F2} ticks={stats.Count} observers=64");
            NoteMetrics($"debug-observe-p99 p99_ms={stats.P99:F2} p50_ms={stats.P50:F2} max_ms={stats.Max:F2} ticks={stats.Count} observers=64");
            Assert.True(stats.P99 < 200, $"Debug observe p99 {stats.P99:F2} ms over 200 ms budget (n={stats.Count}, p50={stats.P50:F2}, max={stats.Max:F2})");
        }
        finally
        {
            await shutdown.CancelAsync();
            await bench.World.WaitSimulationAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    static Task[] StartObservers(ZoneWorld world, int objectId, int observers, CancellationToken stop)
    {
        var character = new CharacterId(objectId.ToString());
        var correlation = new CorrelationId("tick-observe");
        var tasks = new Task[observers];
        for (int i = 0; i < observers; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    if (!world.TryGetPublishedObservation(objectId, out var snapshot))
                        continue;
                    AgentObservationProjector.Project(snapshot, character, correlation);
                }
            }, CancellationToken.None);
        }

        return tasks;
    }

    static async Task WaitForTicksAsync(TickRecorder recorder, int needed, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (recorder.WindowCount < needed)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for {needed} ticks (got {recorder.WindowCount}).");
            await Task.Delay(50);
        }
    }

    static async Task<SceneMeasurement> MeasureSceneAsync(string map, int npcCount)
    {
        var bench = new WorldBench(aoi: new RangeAoi(4_000), spawns: Cluster(map, npcCount), mapId: map);
        var player = await bench.EnterAsync();
        var snapshot = await bench.World.PublishObservationAsync(player.Session, CancellationToken.None);
        Assert.NotNull(snapshot);
        AssertKnownlistParity(player.Player, snapshot!);
        var result = AgentObservationProjector.Project(
            snapshot!,
            new CharacterId(player.Session.ObjectId.ToString()),
            new CorrelationId($"measure-{map}"));
        Assert.True(result.Ok, result.Detail);
        int bytes = AgentContractJson.MeasureUtf8(result.Observation!);
        return new SceneMeasurement(map, snapshot!.Visible.Length, result.Observation!.Entities.Count, result.Observation.TotalVisible, result.Observation.HasMore, bytes);
    }

    static void AssertKnownlistParity(ZoneEntity player, Fineage.Zone.Observation.ZoneObservationSnapshot snapshot)
    {
        var known = player.Knownlist.ToHashSet();
        var visible = snapshot.Visible.Select(v => v.ObjectId).ToHashSet();
        Assert.Equal(known, visible);
        Assert.DoesNotContain(player.ObjectId, visible);
    }

    static (VerifiedAgentIdentity Identity, CharacterId Character, AgentObservationAdapter Adapter) BindAgent(
        WorldBench _,
        BenchPlayer player,
        AgentScope scope,
        string agentName = "agent-observe")
    {
        var store = new MemoryAgentIdentityStore();
        var ownership = new MemoryGameCharacterOwnership();
        var character = new CharacterId(player.Session.ObjectId.ToString());
        ownership.Add(character, player.Session.Account);
        var service = new AgentIdentityService(store, ownership);
        TenantId tenant = service.CreateTenant(new TenantId("tenant-observe"));
        PrincipalId principal = service.CreatePrincipal(tenant, new PrincipalId("principal-observe"));
        AgentId agent = service.CreateAgent(principal, [scope], new AgentId(agentName));
        service.BindCharacter(agent, player.Session.Account, character);
        IssuedAgentCredential issued = service.IssueCredential(agent, DateTimeOffset.UtcNow.AddHours(1));
        VerifiedAgentIdentity identity = service.Authenticate(issued.CredentialId, issued.RevealSecret());
        return (identity, character, new AgentObservationAdapter(service));
    }

    static void NoteMetrics(string line)
    {
        File.AppendAllText("/tmp/obs-1001-metrics.txt", line + Environment.NewLine);
    }

    static SpawnCatalog Cluster(string mapId, int count)
    {
        var defs = new SpawnNpcDefinition[count];
        for (int i = 0; i < count; i++)
        {
            int x = NetTestKit.ElfX + (i % 20) * 20;
            int y = NetTestKit.ElfY + (i / 20) * 20;
            defs[i] = Npc(9_700_000 + i, 97_000 + i, $"TownNpc{i}", x, y, NetTestKit.ElfZ);
        }

        return SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [mapId] = new(mapId, true, defs)
        });
    }

    static SpawnNpcDefinition Npc(int objectId, int npcId, string name, int x, int y, int z) =>
        new(objectId, npcId, name, x, y, z, 0, true, 400, 1f, 1f, 60, 0, "", "Monster",
            Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []);

    sealed record SceneMeasurement(string Map, int Visible, int Entities, int TotalVisible, bool HasMore, int Bytes);

    sealed class TickRecorder : ISimulationObserver
    {
        readonly object _gate = new();
        readonly List<double> _all = [];
        int _windowStart;

        public int WindowCount
        {
            get { lock (_gate) return _all.Count - _windowStart; }
        }

        public void BeginWindow()
        {
            lock (_gate)
                _windowStart = _all.Count;
        }

        public double WindowP99() => WindowStats().P99;

        public (int Count, double P50, double P99, double Max) WindowStats()
        {
            lock (_gate)
            {
                int n = _all.Count - _windowStart;
                if (n <= 0)
                    return (0, 0, 0, 0);
                var sorted = _all.Skip(_windowStart).OrderBy(v => v).ToArray();
                return (sorted.Length, Percentile(sorted, 0.50), Percentile(sorted, 0.99), sorted[^1]);
            }
        }

        static double Percentile(double[] sorted, double p)
        {
            int i = Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1);
            return sorted[i];
        }

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
        }

        public void StageFailed(StageFailure failure)
        {
        }

        public void TicksSkipped(TickContext tick, long skipped)
        {
        }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers)
        {
        }

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
        {
            lock (_gate)
                _all.Add(elapsed.TotalMilliseconds);
        }
    }
}
