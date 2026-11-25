using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>ClientSetTime</c> on enter-world and on game-minute ticks via <see cref="ZoneGameTime"/>.</summary>
public sealed class ClientSetTimeWireTests
{
    [Fact]
    public async Task Enter_burst_includes_client_set_time_with_clock_speed_six()
    {
        var kit = new NetTestKit();
        var (session, entity) = await kit.EnterAsync();
        var self = EntityId.FromObjectId(entity.ObjectId, 1);
        var handler = new SessionLifecyclePacketHandler(
            new MemoryHandoffStore(new HandoffTokens(HandoffTokens.DefaultSecret)),
            kit.World,
            new EntityIdMap(),
            new NetPresenceHub(),
            () => kit.Now,
            _ => 0,
            (_, _, _, _) => Task.CompletedTask);

        var snapshot = handler.CommitEnterForTest(session, self, entity);
        var frames = EnterEncoder.EncodeAndDispatch(snapshot);
        var timeFrame = Assert.Single(frames, f => f.Opcode == NetOpcode.ClientSetTime);
        var decoded = NetCodec.DecodeClientSetTime(timeFrame.Payload);
        Assert.Equal(6, decoded.ClockSpeed);
        var expected = kit.World.RunStepForTest(() => kit.World.BuildClientSetTimeNet(kit.Now.ToUnixTimeMilliseconds()));
        Assert.Equal(expected.GameMinutes, decoded.GameMinutes);
    }

    [Fact]
    public async Task Simulation_resyncs_client_set_time_when_the_game_minute_advances()
    {
        var refMs = 1_800_000_000_000L;
        var (bridge, rec) = NetBridgeRecorder.Create();
        var kit = new NetTestKit(gameTime: new ZoneGameTime(refMs));
        kit.World.NetBridge = bridge;
        _ = await kit.EnterAsync();
        rec.Calls.Clear();

        kit.Now = DateTimeOffset.FromUnixTimeMilliseconds(refMs + ZoneGameTime.MillisPerIgDay / (24 * 60));
        kit.World.RunStepForTest();

        var fanouts = rec.Of(nameof(IWorldEventPublisher.FanoutClientSetTime));
        Assert.NotEmpty(fanouts);
        var msg = (ClientSetTimeNetMsg)fanouts[0][1]!;
        Assert.Equal(6, msg.ClockSpeed);
        Assert.Equal(1, msg.GameMinutes);
    }
}