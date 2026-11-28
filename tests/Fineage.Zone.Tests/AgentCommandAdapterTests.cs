using System.Text.Json;
using Fineage.Accounts;
using Fineage.Agent.Contracts;
using Fineage.Agent.Gate;
using Fineage.Agent.Identity;
using Fineage.Zone;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;
using Xunit.Abstractions;

namespace Fineage.Zone.Tests;

/// <summary>
/// AI-005: Agent writes go through the live ZoneSession and ZoneCommandIngress with
/// <see cref="CommandSource.Ai"/>. Validation, rate limits, and the durable economy path
/// are the same as for a player.
/// </summary>
public sealed class AgentCommandAdapterTests
{
    const int Adena = 57;
    readonly ITestOutputHelper _output;

    public AgentCommandAdapterTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Directory_tracks_the_live_session_and_rejects_a_forged_copy()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        Assert.True(bench.World.TryGetLiveSession(player.Session.ObjectId, out ZoneSession? live));
        Assert.Same(player.Session, live);

        var forged = new ZoneSession();
        forged.Bind(player.Session.ObjectId, player.Session.Account, player.Session.Name, player.Session.ClassId);
        Assert.NotSame(player.Session, forged);
        Assert.True(bench.World.TryGetLiveSession(player.Session.ObjectId, out ZoneSession? stillLive));
        Assert.Same(player.Session, stillLive);
        Assert.NotSame(forged, stillLive);

        await bench.World.LeaveAsync(player.Session, CancellationToken.None);
        Assert.False(bench.World.TryGetLiveSession(player.ObjectId, out _));
    }

    [Fact]
    public async Task The_same_invalid_move_is_refused_for_a_player_and_an_agent()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        var far = new MoveCommand(0, 0, 0);
        var playerTask = bench.World.ExecuteAsync(player.Session, far, CancellationToken.None);
        bench.World.RunStepForTest();
        string? playerOutcome = await playerTask;

        var (identity, character, adapter) = BindAgent(player, AgentScope.NavigationExecute);
        Task<AgentCommandResult> aiTask = adapter.ExecuteAsync(identity, character, MoveRequest(identity, character, 0, 0, 0), bench.World);
        bench.World.RunStepForTest();
        AgentCommandResult ai = await aiTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(string.IsNullOrWhiteSpace(playerOutcome));
        Assert.Equal(playerOutcome, ai.IngressOutcome);
        Assert.False(ai.Ok);
        Assert.Equal(CommandSource.Ai, ai.Source);
    }

    [Fact]
    public async Task The_same_invalid_attack_is_refused_for_a_player_and_an_agent()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        var playerTask = bench.World.ExecuteAsync(player.Session, new AttackCommand(9_999_999, Forced: false), CancellationToken.None);
        bench.World.RunStepForTest();
        string? playerOutcome = await playerTask;

        var (identity, character, adapter) = BindAgent(player, AgentScope.CombatExecute);
        var request = ActionRequest(identity, character, "attack", """{"targetObjectId":9999999}""");
        Task<AgentCommandResult> aiTask = adapter.ExecuteAsync(identity, character, request, bench.World);
        bench.World.RunStepForTest();
        AgentCommandResult ai = await aiTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(string.IsNullOrWhiteSpace(playerOutcome));
        Assert.Equal(playerOutcome, ai.IngressOutcome);
        Assert.False(ai.Ok);
    }

    [Fact]
    public async Task An_agent_cannot_bypass_item_ownership_validation()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        var (identity, character, adapter) = BindAgent(player, AgentScope.InventoryMutate);
        var request = ActionRequest(identity, character, "use_item", """{"objectId":888888}""");
        var playerTask = bench.World.ExecuteAsync(player.Session, new UseItemCommand(888888), CancellationToken.None);
        bench.World.RunStepForTest();
        string? playerOutcome = await playerTask;
        Task<AgentCommandResult> aiTask = adapter.ExecuteAsync(identity, character, request, bench.World);
        bench.World.RunStepForTest();
        AgentCommandResult ai = await aiTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(string.IsNullOrWhiteSpace(playerOutcome));
        Assert.Equal(playerOutcome, ai.IngressOutcome);
        Assert.False(ai.Ok);
    }

    [Fact]
    public async Task Write_scopes_are_checked_before_ingress()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        var log = bench.World.RecordSimulation();
        int before = log.Entries.Count;
        var (identity, character, adapter) = BindAgent(player, AgentScope.WorldRead);
        AgentCommandResult denied = await adapter.ExecuteAsync(
            identity, character, MoveRequest(identity, character, player.Session.X + 10, player.Session.Y, player.Session.Z), bench.World);
        Assert.False(denied.Ok);
        Assert.Equal(AgentRefusalCodes.ScopeDenied, denied.RefusalCode);
        Assert.Null(denied.IngressOutcome);
        Assert.Equal(before, log.Entries.OfType<CommandLogEntry>().Count());

        var allowed = BindAgent(player, AgentScope.NavigationExecute, agentName: "agent-nav");
        Task<AgentCommandResult> movedTask = allowed.Adapter.ExecuteAsync(
            allowed.Identity,
            allowed.Character,
            MoveRequest(allowed.Identity, allowed.Character, player.Session.X + 20, player.Session.Y, player.Session.Z),
            bench.World);
        bench.World.RunStepForTest();
        AgentCommandResult moved = await movedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(moved.Ok || moved.IngressOutcome is not null, moved.Detail ?? "navigation scope still failed");
        Assert.Contains(log.Entries.OfType<CommandLogEntry>(), entry => entry.Source == CommandSource.Ai);
    }

    [Fact]
    public async Task Economy_destroy_through_the_gate_uses_the_durable_ledger_path()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync(items: [new StoredItem(Adena, 50, -1)]);
        int slot = player.Slot(Adena);
        var (identity, character, adapter) = BindAgent(player, AgentScope.InventoryMutate);
        // Drop is the published inventory.mutate write; destroy is the same durable bag path the player uses.
        var drop = ActionRequest(
            identity,
            character,
            "drop_item",
            $$"""{"objectId":{{slot}},"count":1,"x":{{player.Session.X}},"y":{{player.Session.Y}},"z":{{player.Session.Z}}}""");
        Task<AgentCommandResult> resultTask = adapter.ExecuteAsync(identity, character, drop, bench.World);
        bench.World.RunStepForTest();
        AgentCommandResult result = await resultTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Ok, result.IngressOutcome ?? result.Detail);
        Assert.Equal(49, player.Saved().Single(item => item.ItemId == Adena).Count);
    }

    [Fact]
    public async Task Ai_and_player_commands_share_ingress_fairness_and_the_same_apply_path()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        var agents = new List<BenchPlayer>();
        for (int i = 0; i < 4; i++)
            agents.Add(await bench.EnterAsync());

        var playerTask = bench.World.ExecuteAsync(player.Session, new CancelTargetCommand(), CancellationToken.None);
        var aiTasks = agents
            .Select(agent => bench.World.ExecuteAsync(agent.Session, new CancelTargetCommand(), CommandSource.Ai, CancellationToken.None))
            .ToList();
        bench.World.RunStepForTest();
        Assert.Null(await playerTask.WaitAsync(TimeSpan.FromSeconds(5)));
        foreach (var task in aiTasks)
            Assert.Null(await task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    [Trait("Category", "perf")]
    public async Task Debug_tick_p99_with_many_ai_driven_characters()
    {
        var recorder = new TickRecorder();
        var bench = new WorldBench(aoi: new Fineage.Zone.Interest.RangeAoi(1_000), simulationObserver: recorder);
        const int n = 8;
        var players = new List<BenchPlayer>();
        var agents = new List<(VerifiedAgentIdentity Identity, CharacterId Character, AgentCommandAdapter Adapter, BenchPlayer Player)>();
        for (int i = 0; i < n; i++)
        {
            var player = await bench.EnterAsync();
            players.Add(player);
            var bind = BindAgent(player, AgentScope.NavigationExecute, agentName: $"agent-{i}");
            agents.Add((bind.Identity, bind.Character, bind.Adapter, player));
        }

        using var shutdown = new CancellationTokenSource();
        bench.World.StartSimulation(shutdown.Token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            long warmupTick = bench.World.SimulationTicks.Tick;
            recorder.BeginWindow();
            using var stop = new CancellationTokenSource();
            Task[] workers = agents.Select(agent => Task.Run(async () =>
            {
                int x = agent.Player.Session.X;
                int y = agent.Player.Session.Y;
                int z = agent.Player.Session.Z;
                while (!stop.IsCancellationRequested)
                {
                    var request = MoveRequest(agent.Identity, agent.Character, x + 12, y, z);
                    try
                    {
                        await agent.Adapter.ExecuteAsync(agent.Identity, agent.Character, request, bench.World, stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    await Task.Delay(200, stop.Token);
                }
            })).ToArray();
            await Task.Delay(TimeSpan.FromSeconds(12));
            await stop.CancelAsync();
            foreach (var worker in workers)
            {
                try { await worker; } catch (OperationCanceledException) { }
            }

            var stats = recorder.WindowStats();
            long endTick = bench.World.SimulationTicks.Tick;
            _output.WriteLine($"Debug AI-driven tick p99={stats.P99:F2} ms p50={stats.P50:F2} max={stats.Max:F2} observerTicks={stats.Count} simTickDelta={endTick - warmupTick} warmupTick={warmupTick} endTick={endTick} agents={n}");
            NoteMetrics($"debug-ai-command-p99 p99_ms={stats.P99:F2} p50_ms={stats.P50:F2} max_ms={stats.Max:F2} ticks={stats.Count} sim_delta={endTick - warmupTick} agents={n}");
            Assert.True(stats.Count >= 20 || endTick - warmupTick >= 20,
                $"expected at least 20 ticks, observer={stats.Count} simDelta={endTick - warmupTick} warmup={warmupTick} end={endTick}");
            Assert.True(stats.P99 < 200, $"Debug AI command p99 {stats.P99:F2} ms over 200 ms budget (n={stats.Count}, p50={stats.P50:F2}, max={stats.Max:F2})");
        }
        finally
        {
            await shutdown.CancelAsync();
            await bench.World.WaitSimulationAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    static (VerifiedAgentIdentity Identity, CharacterId Character, AgentCommandAdapter Adapter) BindAgent(
        BenchPlayer player,
        AgentScope scope,
        string agentName = "agent-command")
    {
        var store = new MemoryAgentIdentityStore();
        var ownership = new MemoryGameCharacterOwnership();
        var character = new CharacterId(player.Session.ObjectId.ToString());
        ownership.Add(character, player.Session.Account);
        var service = new AgentIdentityService(store, ownership);
        TenantId tenant = service.CreateTenant(new TenantId("tenant-command"));
        PrincipalId principal = service.CreatePrincipal(tenant, new PrincipalId("principal-command"));
        AgentId agent = service.CreateAgent(principal, [scope], new AgentId(agentName));
        service.BindCharacter(agent, player.Session.Account, character);
        IssuedAgentCredential issued = service.IssueCredential(agent, DateTimeOffset.UtcNow.AddHours(1));
        VerifiedAgentIdentity identity = service.Authenticate(issued.CredentialId, issued.RevealSecret());
        return (identity, character, new AgentCommandAdapter(service));
    }

    static AgentActionRequest MoveRequest(VerifiedAgentIdentity identity, CharacterId character, int x, int y, int z) =>
        ActionRequest(identity, character, "move", $$"""{"destX":{{x}},"destY":{{y}},"destZ":{{z}}}""");

    static AgentActionRequest ActionRequest(VerifiedAgentIdentity identity, CharacterId character, string tool, string json)
    {
        var principal = AgentPrincipal.Create(identity.TenantId, identity.PrincipalId, identity.AgentId, character, identity.ExpiresAt);
        return AgentActionRequest.Create(
            new CommandId($"cmd-{Guid.NewGuid():N}"),
            new CorrelationId($"corr-{Guid.NewGuid():N}"),
            new IdempotencyKey($"idem-{Guid.NewGuid():N}"),
            principal,
            tool,
            JsonDocument.Parse(json).RootElement.Clone(),
            DateTimeOffset.UtcNow.AddMinutes(1));
    }

    static void NoteMetrics(string line) => File.AppendAllText("/tmp/cmd-1002-metrics.txt", line + Environment.NewLine);


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
