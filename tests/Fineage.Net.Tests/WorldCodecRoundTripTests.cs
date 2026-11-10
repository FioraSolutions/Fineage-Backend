using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Mandatory encode/decode round-trips for core world/session messages (#353 NetCodec track).
/// Keeps the codec monolith honest without a big-bang domain split.
/// </summary>
public sealed class WorldCodecRoundTripTests
{
    [Fact]
    public void MoveState_roundtrip()
    {
        var m = new MoveStateMsg
        {
            EntityValue = 42,
            EntityGeneration = 3,
            OriginX = 1,
            OriginY = 2,
            OriginZ = 3,
            DestX = 10,
            DestY = 20,
            DestZ = 30,
            Running = false
        };
        var back = NetCodec.DecodeMoveState(NetCodec.EncodeMoveState(m));
        Assert.Equal(42uL, back.EntityValue);
        Assert.Equal(3u, back.EntityGeneration);
        Assert.Equal(1, back.OriginX);
        Assert.Equal(20, back.DestY);
        Assert.False(back.Running);
    }

    [Fact]
    public void EntityDespawn_and_PositionCorrection_roundtrip()
    {
        var despawn = NetCodec.DecodeEntityDespawn(NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = 7,
            EntityGeneration = 1
        }));
        Assert.Equal(7uL, despawn.EntityValue);
        Assert.Equal(1u, despawn.EntityGeneration);

        var fix = NetCodec.DecodePositionCorrection(NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
        {
            EntityValue = 9,
            EntityGeneration = 2,
            X = 100,
            Y = 200,
            Z = 300,
            Heading = 400
        }));
        Assert.Equal(9uL, fix.EntityValue);
        Assert.Equal(100, fix.X);
        Assert.Equal(400, fix.Heading);
    }

    [Fact]
    public void Reject_and_ZoneAuthResult_roundtrip()
    {
        var reject = NetCodec.DecodeReject(NetCodec.EncodeReject(new RejectMsg
        {
            Code = "not_in_world",
            Message = "Enter world first."
        }));
        Assert.Equal("not_in_world", reject.Code);
        Assert.Equal("Enter world first.", reject.Message);

        var ok = NetCodec.DecodeZoneAuthResult(NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
        {
            Ok = true,
            EntityValue = 55,
            EntityGeneration = 4
        }));
        Assert.True(ok.Ok);
        Assert.Equal(55uL, ok.EntityValue);
        Assert.Equal(4u, ok.EntityGeneration);

        var fail = NetCodec.DecodeZoneAuthResult(NetCodec.EncodeZoneAuthResult(new ZoneAuthResultMsg
        {
            Ok = false,
            Code = "handoff_expired",
            Message = "Handoff token has expired."
        }));
        Assert.False(fail.Ok);
        Assert.Equal("handoff_expired", fail.Code);
    }

    [Fact]
    public void Hello_roundtrip()
    {
        var hello = NetCodec.DecodeHello(NetCodec.EncodeHello(new HelloMsg
        {
            ClientName = "fineage-client",
            FeatureFlags = ProtocolCapabilities.FeatureSession
        }));
        Assert.Equal("fineage-client", hello.ClientName);
        Assert.Equal(ProtocolCapabilities.FeatureSession, hello.FeatureFlags);

        var ack = NetCodec.DecodeHelloAck(NetCodec.EncodeHelloAck(new HelloAckMsg
        {
            FeatureFlags = ProtocolCapabilities.FeatureWorldPresence,
            ServerName = "fineage-zone-net"
        }));
        Assert.Equal("fineage-zone-net", ack.ServerName);
        Assert.Equal(ProtocolCapabilities.FeatureWorldPresence, ack.FeatureFlags);
    }
}
