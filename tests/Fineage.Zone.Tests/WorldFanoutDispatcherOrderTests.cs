using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Async <see cref="FanoutDispatcher"/> must not reorder sealed batches or per-batch sends.
/// </summary>
public sealed class WorldFanoutDispatcherOrderTests
{
    [Fact]
    public async Task Handler_reply_completes_after_command_commit_and_fanout_stays_in_apply_order_per_connection()
    {
        var (kit, session, peer, recorder) = await SetupAsync();
        var barriers = new List<int>();
        lock (recorder.Calls)
            recorder.Calls.Clear();

        for (int step = 1; step <= 2; step++)
        {
            var reply = kit.World.ExecuteAsync(session, new SelectTargetCommand(peer.ObjectId), CancellationToken.None);
            Assert.False(reply.IsCompleted);
            kit.World.RunStepForTest();
            Assert.True(reply.IsCompleted, "handler reply completes once the command batch is sealed");
            Assert.Null(await reply);
            barriers.Add(step);
        }

        List<(string Method, object?[] Args)> fanout;
        lock (recorder.Calls)
            // Self MyTargetSelected mirror only (selector 0). Peer TargetSelected is a second fanout per click.
            fanout = recorder.Calls
                .Where(c => c.Method == nameof(IWorldEventPublisher.FanoutTargetSelected) && IsSelfTargetSelected(c.Args))
                .ToList();

        Assert.Equal(2, fanout.Count);
        Assert.Equal(peer.ObjectId, fanout[0].Args[1]);
        Assert.Equal(peer.ObjectId, fanout[1].Args[1]);
        Assert.Equal(2, barriers.Count);
        lock (recorder.Calls)
            Assert.Equal(2, recorder.Calls.Count(c =>
                c.Method == nameof(IWorldEventPublisher.FanoutTargetSelected) && !IsSelfTargetSelected(c.Args)));
    }

    [Fact]
    public async Task Three_command_steps_fan_out_TargetSelected_in_command_order()
    {
        var (kit, session, peer, recorder) = await SetupAsync();
        var (_, peer2) = await kit.EnterAsync(x: NetTestKit.ElfX + 250);
        var (_, peer3) = await kit.EnterAsync(x: NetTestKit.ElfX + 500);
        int[] sequence = [peer.ObjectId, peer2.ObjectId, peer3.ObjectId];

        lock (recorder.Calls)
            recorder.Calls.Clear();

        foreach (int target in sequence)
        {
            var reply = kit.World.ExecuteAsync(session, new SelectTargetCommand(target), CancellationToken.None);
            kit.World.RunStepForTest();
            Assert.Null(await reply);
        }

        List<int> recorded;
        lock (recorder.Calls)
            recorded = recorder.Calls
                .Where(c => c.Method == nameof(IWorldEventPublisher.FanoutTargetSelected) && IsSelfTargetSelected(c.Args))
                .Select(c => (int)c.Args[1]!)
                .ToList();

        Assert.Equal(sequence, recorded);
    }

    /// <summary>SelectorObjectId == 0 (or omitted) is the owner's MyTargetSelected mirror.</summary>
    static bool IsSelfTargetSelected(object?[] args) =>
        args.Length <= 3 || args[3] is null or 0;

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Peer, NetBridgeRecorder Recorder)> SetupAsync()
    {
        var kit = new NetTestKit(simulationSeed: 3);
        var (session, _) = await kit.EnterAsync();
        var (_, peer) = await kit.EnterAsync(x: NetTestKit.ElfX + 100);
        var (bridge, recorder) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, peer, recorder);
    }
}