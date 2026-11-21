using Fineage.Protocol;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Step 5 of <c>docs/architecture/zone-simulation.md</c>: a run is its seed plus its command log. Replaying the log on a
/// world built the same way reproduces the run's state and fan-out, and a replay that drifts names the entry.
/// </summary>
public sealed class SimulationReplayTests
{
    const int FirstMobId = 9_800_001;
    const int MobCount = 3;
    const int WindStrike = 1177;
    const int UnknownSkill = 999_999;

    /// <summary>Wall advances that are not tick multiples, one late step and one step while the wall clock is behind.</summary>
    static readonly int[] WallAdvances = [200, 130, 270, 200, 650, 0, 200, 90, 310, 200];

    [Fact]
    public async Task Replaying_the_command_log_reproduces_state_and_fanout()
    {
        var recorded = await RecordAsync(seed: 20_261_120);
        Assert.Contains(recorded.Log.Entries, e => e is CommandLogEntry { Outcome: "ok" });
        Assert.Contains(recorded.Log.Entries, e => e is CommandLogEntry { Outcome: not "ok" });
        Assert.True(recorded.Log.Entries.OfType<StepLogEntry>().Count() >= 5,
            "replay fixture must include multiple steps so drift is observable");

        var (kit, _, recorder, tracked) = await SetupAsync(recorded.Log.Header.Seed);
        await kit.World.ReplayAsync(recorded.Log, ms => kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(ms), CancellationToken.None);

        Assert.Equal(recorded.State, CaptureState(kit, tracked));
        Assert.Equal(recorded.Fanout, CaptureFanout(recorder));
    }

    [Fact]
    public async Task Identical_seeds_record_identical_logs_and_another_seed_rolls_another_run()
    {
        var first = await RecordAsync(seed: 5);
        var second = await RecordAsync(seed: 5);
        var other = await RecordAsync(seed: 6);

        Assert.Equal(first.Log.Entries, second.Log.Entries);
        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Fanout, second.Fanout);
        Assert.NotEqual(first.State, other.State);
    }

    [Fact]
    public async Task Replay_refuses_a_world_with_another_seed()
    {
        var recorded = await RecordAsync(seed: 11);
        var (kit, _, _, _) = await SetupAsync(seed: 12);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kit.World.ReplayAsync(recorded.Log, ms => kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(ms), CancellationToken.None));
        Assert.Contains("matching the recording", error.Message);
        Assert.Contains("Seed = 11", error.Message);
    }

    [Fact]
    public async Task A_replay_whose_outcome_drifts_names_the_diverging_entry()
    {
        var recorded = await RecordAsync(seed: 13);
        var tampered = new SimulationLog(recorded.Log.Header);
        long changed = -1;
        foreach (var entry in recorded.Log.Entries)
        {
            switch (entry)
            {
                case StepLogEntry step:
                    tampered.AppendStep(step.Tick, step.WallUnixMs);
                    break;
                case CommandLogEntry command:
                    bool tamper = changed < 0 && command.Outcome == "ok";
                    if (tamper)
                        changed = command.Sequence;
                    tampered.AppendCommand(command.Tick, command.WallUnixMs, command.ActorObjectId, command.Command,
                        tamper ? "target out of range" : command.Outcome);
                    break;
            }
        }

        var (kit, _, _, _) = await SetupAsync(recorded.Log.Header.Seed);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            kit.World.ReplayAsync(tampered, ms => kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(ms), CancellationToken.None));
        Assert.StartsWith($"replay diverged at entry {changed} ", error.Message);
        Assert.Contains("returned 'ok'", error.Message);
    }

    [Fact]
    public async Task Commands_wait_for_the_next_step_and_are_logged_with_its_tick_before_it()
    {
        var (kit, session, _, _) = await SetupAsync(seed: 1);
        var log = kit.World.RecordSimulation();
        long startTick = kit.World.SimulationTicks.Tick;

        var rejected = kit.World.ExecuteAsync(session, new SkillUseCommand(UnknownSkill, FirstMobId, false), CancellationToken.None);
        var accepted = kit.World.ExecuteAsync(session, new SelectTargetCommand(FirstMobId), CancellationToken.None);
        Assert.False(rejected.IsCompleted);
        Assert.Equal(0, session.SelectedTargetId);

        kit.Now = kit.Now.AddMilliseconds(250);
        kit.World.StepSimulation();

        Assert.NotNull(await rejected);
        Assert.Null(await accepted);
        Assert.Equal(FirstMobId, session.SelectedTargetId);
        long wall = kit.Now.ToUnixTimeMilliseconds();
        var entries = log.Entries;
        var first = Assert.IsType<CommandLogEntry>(entries[0]);
        Assert.Equal((0L, startTick + 1, wall, session.ObjectId, "skill-use"),
            (first.Sequence, first.Tick, first.WallUnixMs, first.ActorObjectId, first.Command.Name));
        Assert.Equal(new SkillUseCommand(UnknownSkill, FirstMobId, false), first.Command);
        Assert.NotEqual("ok", first.Outcome);
        Assert.Equal("ok", Assert.IsType<CommandLogEntry>(entries[1]).Outcome);
        Assert.Equal(new StepLogEntry(2, startTick + 1, wall), entries[2]);
    }

    sealed record Recording(SimulationLog Log, List<string> State, List<string> Fanout);

    /// <summary>A mystic targets, casts at and melees a mob while others random-walk, with moves and a stop in between.</summary>
    static async Task<Recording> RecordAsync(ulong seed)
    {
        var (kit, session, recorder, tracked) = await SetupAsync(seed);
        var log = kit.World.RecordSimulation();
        var script = new Dictionary<int, ZoneCommand[]>
        {
            [0] = [new SelectTargetCommand(FirstMobId), new SkillUseCommand(WindStrike, FirstMobId, false)],
            [2] = [new SkillUseCommand(WindStrike, FirstMobId, false)],
            [3] = [new AttackCommand(FirstMobId, false)],
            [5] = [new MoveCommand(NetTestKit.ElfX - 150, NetTestKit.ElfY + 40, NetTestKit.ElfZ)],
            [6] = [new StopMoveCommand(), new SkillUseCommand(UnknownSkill, FirstMobId, false)],
            [7] = [new CancelTargetCommand(), new SelectTargetCommand(FirstMobId + 1), new AttackCommand(FirstMobId + 1, false)],
        };

        for (int round = 0; round < 6; round++)
        {
            for (int i = 0; i < WallAdvances.Length; i++)
            {
                var replies = new List<Task<string?>>();
                if (round == 0 && script.TryGetValue(i, out var commands))
                {
                    foreach (var command in commands)
                        replies.Add(kit.World.ExecuteAsync(session, command, CancellationToken.None));
                }

                kit.Now = kit.Now.AddMilliseconds(WallAdvances[i]);
                kit.World.StepSimulation();
                await Task.WhenAll(replies);
            }
        }

        return new Recording(log, CaptureState(kit, tracked), CaptureFanout(recorder));
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, NetBridgeRecorder Recorder, int[] Tracked)> SetupAsync(ulong seed)
    {
        var kit = new NetTestKit(Map(), "replay", regions: GridRegionPartition.MapTiles, simulationSeed: seed);
        var (session, player) = await kit.EnterAsync();
        var (_, peer) = await kit.EnterAsync(x: NetTestKit.ElfX + 300);
        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        lock (recorder.Calls)
            recorder.Calls.Clear();
        int[] tracked = [player.ObjectId, peer.ObjectId, .. Enumerable.Range(FirstMobId, MobCount)];
        return (kit, session, recorder, tracked);
    }

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

            string abnormals = string.Join(",", e.Abnormals.Select(a => $"{a.Id}:{a.ExpiresAtUnixMs}"));
            state.Add($"{id} hp={e.CurHp} mp={e.CurMp} x={e.X} y={e.Y} heading={e.Heading} move={e.MoveActive} " +
                      $"dead={e.Dead} ab=[{abnormals}]");
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
        StatusUpdateMsg s => $"status {s.ObjectId} {string.Join(",", s.AttributeIds)}={string.Join(",", s.Values)}",
        _ => arg?.ToString() ?? "null"
    };

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["replay"] = new("replay", true,
                Enumerable.Range(0, MobCount).Select(i =>
                    new SpawnNpcDefinition(FirstMobId + i, 98000 + i, "Gremlin", NetTestKit.ElfX + 300 + i * 120,
                        NetTestKit.ElfY + 60, NetTestKit.ElfZ, 0, true, 300, 1f, 1f, 60, 0, "", "Monster",
                        Level: 1 + i, HpRegen: 2.5, MpRegen: 1.2, MaxMp: 100,
                        AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []))
                    .ToList())
        });
}
