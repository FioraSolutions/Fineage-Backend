using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class MountCodecRoundTripTests
{
    [Fact]
    public void Ride_and_player_action_state_roundtrip()
    {
        var ride = NetCodec.DecodeRide(NetCodec.EncodeRide(new RideNetMsg
        {
            ObjectId = 9,
            Action = Fineage.Protocol.MountProtocol.RideMount,
            MountKind = 1,
            RideNpcId = 1_125_026
        }));
        Assert.Equal((9, 1, (byte)1, 1_125_026),
            (ride.ObjectId, ride.Action, ride.MountKind, ride.RideNpcId));

        var state = NetCodec.DecodePlayerActionState(NetCodec.EncodePlayerActionState(new PlayerActionStateNetMsg
        {
            PetOut = true,
            MountablePet = true,
            Mounted = false,
            CanRide = true,
            MountKind = 0,
            PetControlObjectId = 42
        }));
        Assert.Equal((true, true, false, true, (byte)0, 42),
            (state.PetOut, state.MountablePet, state.Mounted, state.CanRide, state.MountKind, state.PetControlObjectId));
    }
}