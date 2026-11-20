using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class WorldTickOutboxTests
{
    [Fact]
    public void Status_update_coalesces_to_last_write_per_subject()
    {
        var (inner, rec) = NetBridgeRecorder.Create();
        var outbox = new WorldTickOutbox();
        IWorldEventPublisher bridge = new OutboxingWorldEventPublisher(inner, outbox);

        outbox.Begin();
        bridge.FanoutStatusUpdate([1], new StatusUpdateMsg { ObjectId = 42, AttributeIds = [9], Values = [10] });
        bridge.FanoutStatusUpdate([1], new StatusUpdateMsg { ObjectId = 42, AttributeIds = [9], Values = [99] });
        outbox.EndRecording();
        outbox.Flush(inner);

        var updates = rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate));
        var row = Assert.Single(updates);
        var status = Assert.IsType<StatusUpdateMsg>(row[1]);
        Assert.Equal(99, status.Values![0]);
    }

    [Fact]
    public void Ordered_events_keep_sequence_and_run_before_coalesced_flush()
    {
        var (inner, rec) = NetBridgeRecorder.Create();
        var outbox = new WorldTickOutbox();
        IWorldEventPublisher bridge = new OutboxingWorldEventPublisher(inner, outbox);

        outbox.Begin();
        bridge.FanoutAttackStance([1], 7, true);
        bridge.FanoutStatusUpdate([1], new StatusUpdateMsg { ObjectId = 7, AttributeIds = [9], Values = [1] });
        bridge.FanoutStatusUpdate([1], new StatusUpdateMsg { ObjectId = 7, AttributeIds = [9], Values = [2] });
        outbox.EndRecording();
        outbox.Flush(inner);

        var methods = rec.Calls.Select(c => c.Method).ToList();
        var firstStance = methods.IndexOf(nameof(IWorldEventPublisher.FanoutAttackStance));
        var lastStatus = methods.LastIndexOf(nameof(IWorldEventPublisher.FanoutStatusUpdate));
        Assert.True(firstStance >= 0);
        Assert.True(lastStatus > firstStance);
        Assert.Equal(1, rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)).Count);
    }
}