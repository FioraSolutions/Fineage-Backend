using Fineage.Accounts;
using Fineage.Zone.Simulation;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Step 6 of <c>docs/architecture/zone-simulation.md</c>: handlers submit commands that the next step applies in
/// submission order, a reply follows the fan-out and persistence of the step that applied it, and the rolls handlers
/// used to make from process-wide randomness draw from the seeded streams.
/// </summary>
public sealed class ZoneCommandIngressTests
{
    const int Saber = 123;
    const int BlessedD = 6575;

    [Fact]
    public async Task Submission_wake_signal_does_not_accumulate_without_a_pump()
    {
        var (kit, session, _, _) = await SetupAsync();
        using var cts = new CancellationTokenSource();
        for (int i = 0; i < 10_000; i++)
            _ = kit.World.ExecuteAsync(session, new CancelTargetCommand(), cts.Token);
        Assert.InRange(kit.World.SubmissionWakeCountForTest, 0, 1);
    }

    [Fact]
    public async Task The_live_loop_applies_a_queued_command_before_the_next_tick()
    {
        var (kit, session, peer, _) = await SetupAsync();
        using var cts = new CancellationTokenSource();
        kit.World.StartSimulation(cts.Token);
        try
        {
            await Task.Delay(30);
            long tick = kit.World.SimulationTicks.Tick;

            var started = System.Diagnostics.Stopwatch.StartNew();
            var select = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), CancellationToken.None);
            var cancel = kit.World.ExecuteAsync(session, new CancelTargetCommand(), CancellationToken.None);
            await Task.WhenAny(Task.WhenAll(select, cancel), Task.Delay(TimeSpan.FromMilliseconds(120)));

            Assert.True(select.IsCompletedSuccessfully && cancel.IsCompletedSuccessfully,
                $"admission waited {started.ElapsedMilliseconds} ms for the next tick");
            Assert.Null(await select);
            Assert.Null(await cancel);
            Assert.Equal(0, session.SelectedTargetId);
            Assert.Equal(tick, kit.World.SimulationTicks.Tick);
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    [Fact]
    public async Task Commands_apply_in_submission_order_within_one_step()
    {
        var (kit, session, peer, _) = await SetupAsync();

        var select = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), CancellationToken.None);
        var cancel = kit.World.ExecuteAsync(session, new CancelTargetCommand(), CancellationToken.None);
        kit.World.RunStepForTest();
        Assert.Null(await select);
        Assert.Null(await cancel);
        Assert.Equal(0, session.SelectedTargetId);

        var cancelFirst = kit.World.ExecuteAsync(session, new CancelTargetCommand(), CancellationToken.None);
        var selectLast = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), CancellationToken.None);
        kit.World.RunStepForTest();
        await Task.WhenAll(cancelFirst, selectLast);
        Assert.Equal(peer.ObjectId, session.SelectedTargetId);
    }

    [Fact]
    public async Task A_cancelled_submission_is_neither_applied_nor_logged()
    {
        var (kit, session, peer, _) = await SetupAsync();
        var log = kit.World.RecordSimulation();
        using var cts = new CancellationTokenSource();

        var reply = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), cts.Token);
        cts.Cancel();
        kit.World.RunStepForTest();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reply);
        Assert.Equal(0, session.SelectedTargetId);
        Assert.DoesNotContain(log.Entries, e => e is CommandLogEntry);
    }

    [Fact]
    public async Task A_handler_reply_is_not_blocked_on_stages_after_commands()
    {
        var (kit, session, peer, _) = await SetupAsync();
        var reply = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), CancellationToken.None);
        Assert.False(reply.IsCompleted);
        kit.World.RunStepForTest();
        Assert.True(reply.IsCompleted);
    }

    [Fact]
    public async Task A_reply_completes_when_the_command_batch_is_committed_and_fanout_drains_in_step_order()
    {
        var (kit, session, peer, recorder) = await SetupAsync();
        lock (recorder.Calls)
            recorder.Calls.Clear();

        var reply = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), CancellationToken.None);
        Assert.False(reply.IsCompleted);
        kit.World.RunStepForTest();
        Assert.True(reply.IsCompleted);
        Assert.Null(await reply);

        lock (recorder.Calls)
        {
            Assert.Contains(recorder.Calls,
                c => c.Method == nameof(IWorldEventPublisher.FanoutTargetSelected) && Equals(c.Args[1], peer.ObjectId));
        }
    }

    [Fact]
    public async Task A_stopped_simulation_fails_pending_commands_and_refuses_new_ones_with_the_reason()
    {
        var (kit, session, peer, _) = await SetupAsync();
        var pending = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), CancellationToken.None);

        kit.World.StartSimulation(new CancellationToken(canceled: true));

        var stranded = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => pending);
        Assert.Matches("^command select-target for object \\d+ was not applied: the zone simulation stopped at tick \\d+$",
            stranded.Message);
        var refused = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            kit.World.ExecuteAsync(session, new CancelTargetCommand(), CancellationToken.None));
        Assert.Matches("^command cancel-target for object \\d+ refused: the zone simulation stopped at tick \\d+$",
            refused.Message);
        Assert.Equal(0, session.SelectedTargetId);
    }

    [Fact]
    public async Task Position_validation_is_a_command_that_reports_the_drift()
    {
        var (kit, session, _, _) = await SetupAsync();

        var close = kit.World.ExecuteAsync(session, new ValidatePositionCommand(session.X, session.Y, session.Z),
            CancellationToken.None);
        var far = kit.World.ExecuteAsync(session, new ValidatePositionCommand(session.X + 2_000, session.Y, session.Z),
            CancellationToken.None);
        Assert.False(far.IsCompleted);
        kit.World.RunStepForTest();

        Assert.Null(await close);
        Assert.Matches("^position drift dxy=\\d+ dz=\\d+$", await far);
    }

    [Fact]
    public void Command_names_derive_from_the_record_type()
    {
        Assert.Equal("validate-position", new ValidatePositionCommand(0, 0, 0).Name);
        Assert.Equal("party-change-leader", new PartyChangeLeaderCommand(1).Name);
    }

    [Fact]
    public async Task Enchant_rolls_draw_from_the_enchanting_characters_region_stream()
    {
        const ulong seed = 31;
        var region = GridRegionPartition.MapTiles.RegionOf(0, BenchX, BenchY);

        var seeded = await EnchantLevelsAsync(new WorldBench(simulationSeed: seed));
        var predicted = await EnchantLevelsAsync(
            new WorldBench(enchantRandom: new SeededRegionRandom(seed, RandomPurpose.Enchant).For(region)));
        var otherSeed = await EnchantLevelsAsync(new WorldBench(simulationSeed: seed + 1));

        Assert.Equal(predicted, seeded);
        Assert.Contains(0, seeded);
        Assert.Contains(seeded, level => level > 4);
        Assert.NotEqual(seeded, otherSeed);
    }

    const int BenchX = 46045, BenchY = 41251;

    /// <summary>Ten blessed scrolls on a +4 saber: each success raises it, each failure resets it to 0.</summary>
    static async Task<List<int>> EnchantLevelsAsync(WorldBench bench)
    {
        const int attempts = 10;
        var scrolls = Enumerable.Repeat(0, attempts).Select(_ => new StoredItem(BlessedD, 1, -1)).ToArray();
        var smith = await bench.EnterAsync(items: [new StoredItem(Saber, 1, -1, 4), .. scrolls]);
        int saber = smith.Slot(Saber);
        var levels = new List<int>();
        for (int i = 0; i < attempts; i++)
        {
            int scroll = smith.Inventory.Items.First(item => item.ItemId == BlessedD).ObjectId;
            Assert.Null(await bench.World.TryEnchantRequestAsync(smith.Session, scroll, default));
            Assert.Null(await bench.World.TryEnchantConfirmAsync(smith.Session, saber, default));
            levels.Add(smith.Inventory.Find(saber)!.EnchantLevel);
        }

        return levels;
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Peer, NetBridgeRecorder Recorder)> SetupAsync()
    {
        var kit = new NetTestKit(simulationSeed: 1);
        var (session, _) = await kit.EnterAsync();
        var (_, peer) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, peer, recorder);
    }
}
