using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class VehicleCodecRoundTripTests
{
    [Fact]
    public void Vehicle_info_and_departure_roundtrip()
    {
        var info = NetCodec.DecodeVehicleInfo(NetCodec.EncodeVehicleInfo(new VehicleInfoNetMsg
        {
            BoatObjectId = 42,
            X = 48950,
            Y = 190613,
            Z = -3610,
            Heading = 60800
        }));
        Assert.Equal((42, 48950, 190613, -3610, 60800), (info.BoatObjectId, info.X, info.Y, info.Z, info.Heading));

        var dep = NetCodec.DecodeVehicleDeparture(NetCodec.EncodeVehicleDeparture(new VehicleDepartureNetMsg
        {
            BoatObjectId = 42,
            MoveSpeed = 800,
            RotationSpeed = 4000,
            DestX = 100,
            DestY = 200,
            DestZ = -3000
        }));
        Assert.Equal((42, 800, 4000, 100, 200, -3000),
            (dep.BoatObjectId, dep.MoveSpeed, dep.RotationSpeed, dep.DestX, dep.DestY, dep.DestZ));
    }

    [Fact]
    public void Vehicle_opcodes_are_the_mobius_interlude_ids()
    {
        Assert.Equal(0x59, NetOpcode.VehicleInfo);
        Assert.Equal(0x5A, NetOpcode.VehicleDeparture);
        Assert.Equal(0x5B, NetOpcode.VehicleCheckLocation);
        Assert.Equal(0x5C, NetOpcode.GetOnVehicle);
        Assert.Equal(0x5D, NetOpcode.GetOffVehicle);
        Assert.Equal(0x71, NetOpcode.MoveToLocationInVehicle);
        Assert.Equal(0x72, NetOpcode.StopMoveInVehicle);
        Assert.Equal(0x73, NetOpcode.ValidateLocationInVehicle);
        Assert.Equal(0xC0, NetOpcode.VehicleStarted);
        Assert.Equal(0x42, NetOpcode.ClientPackets.RequestGetOnVehicle);
        Assert.Equal(0x43, NetOpcode.ClientPackets.RequestGetOffVehicle);
        Assert.Equal(0x5C, NetOpcode.ClientPackets.RequestMoveToLocationInVehicle);
        Assert.Equal(0x5D, NetOpcode.ClientPackets.CannotMoveAnymoreInVehicle);
        Assert.NotEqual(0x03E1, NetOpcode.VehicleInfo);
        Assert.NotEqual(0x03E2, NetOpcode.VehicleDeparture);
    }

    [Fact]
    public void Remaining_vehicle_packets_roundtrip_in_mobius_field_order()
    {
        var check = NetCodec.DecodeVehicleCheckLocation(NetCodec.EncodeVehicleCheckLocation(new VehicleCheckLocationNetMsg
        {
            BoatObjectId = 7,
            X = 1,
            Y = 2,
            Z = 3,
            Heading = 4
        }));
        Assert.Equal((7, 1, 2, 3, 4), (check.BoatObjectId, check.X, check.Y, check.Z, check.Heading));

        var on = NetCodec.DecodeGetOnVehicle(NetCodec.EncodeGetOnVehicle(new GetOnVehicleNetMsg
        {
            PlayerObjectId = 8,
            BoatObjectId = 9,
            X = 10,
            Y = 11,
            Z = 12
        }));
        Assert.Equal((8, 9, 10, 11, 12), (on.PlayerObjectId, on.BoatObjectId, on.X, on.Y, on.Z));

        var off = NetCodec.DecodeGetOffVehicle(NetCodec.EncodeGetOffVehicle(new GetOffVehicleNetMsg
        {
            PlayerObjectId = 8,
            BoatObjectId = 9,
            X = 13,
            Y = 14,
            Z = 15
        }));
        Assert.Equal((8, 9, 13, 14, 15), (off.PlayerObjectId, off.BoatObjectId, off.X, off.Y, off.Z));

        var move = NetCodec.DecodeMoveToLocationInVehicle(NetCodec.EncodeMoveToLocationInVehicle(new MoveToLocationInVehicleNetMsg
        {
            PlayerObjectId = 1,
            BoatObjectId = 2,
            DestX = 3,
            DestY = 4,
            DestZ = 5,
            OriginX = 6,
            OriginY = 7,
            OriginZ = 8
        }));
        Assert.Equal((1, 2, 3, 4, 5, 6, 7, 8),
            (move.PlayerObjectId, move.BoatObjectId, move.DestX, move.DestY, move.DestZ, move.OriginX, move.OriginY, move.OriginZ));

        var stop = NetCodec.DecodeStopMoveInVehicle(NetCodec.EncodeStopMoveInVehicle(new StopMoveInVehicleNetMsg
        {
            PlayerObjectId = 1,
            BoatObjectId = 2,
            X = 3,
            Y = 4,
            Z = 5,
            Heading = 6
        }));
        Assert.Equal((1, 2, 3, 4, 5, 6), (stop.PlayerObjectId, stop.BoatObjectId, stop.X, stop.Y, stop.Z, stop.Heading));

        var validate = NetCodec.DecodeValidateLocationInVehicle(NetCodec.EncodeValidateLocationInVehicle(new ValidateLocationInVehicleNetMsg
        {
            PlayerObjectId = 1,
            BoatObjectId = 2,
            X = 3,
            Y = 4,
            Z = 5,
            Heading = 6
        }));
        Assert.Equal((1, 2, 3, 4, 5, 6), (validate.PlayerObjectId, validate.BoatObjectId, validate.X, validate.Y, validate.Z, validate.Heading));

        var started = NetCodec.DecodeVehicleStarted(NetCodec.EncodeVehicleStarted(new VehicleStartedNetMsg
        {
            BoatObjectId = 4,
            State = 1
        }));
        Assert.Equal((4, 1), (started.BoatObjectId, started.State));

        var reqOn = NetCodec.DecodeRequestGetOnVehicle(NetCodec.EncodeRequestGetOnVehicle(new RequestGetOnVehicleNetMsg
        {
            BoatObjectId = 9,
            X = 1,
            Y = 2,
            Z = 3
        }));
        Assert.Equal((9, 1, 2, 3), (reqOn.BoatObjectId, reqOn.X, reqOn.Y, reqOn.Z));

        var reqOff = NetCodec.DecodeRequestGetOffVehicle(NetCodec.EncodeRequestGetOffVehicle(new RequestGetOffVehicleNetMsg
        {
            BoatObjectId = 9,
            X = 4,
            Y = 5,
            Z = 6
        }));
        Assert.Equal((9, 4, 5, 6), (reqOff.BoatObjectId, reqOff.X, reqOff.Y, reqOff.Z));

        var reqMove = NetCodec.DecodeRequestMoveToLocationInVehicle(NetCodec.EncodeRequestMoveToLocationInVehicle(
            new RequestMoveToLocationInVehicleNetMsg
            {
                BoatObjectId = 2,
                DestX = 3,
                DestY = 4,
                DestZ = 5,
                OriginX = 6,
                OriginY = 7,
                OriginZ = 8
            }));
        Assert.Equal((2, 3, 4, 5, 6, 7, 8),
            (reqMove.BoatObjectId, reqMove.DestX, reqMove.DestY, reqMove.DestZ, reqMove.OriginX, reqMove.OriginY, reqMove.OriginZ));

        var stuck = NetCodec.DecodeCannotMoveAnymoreInVehicle(NetCodec.EncodeCannotMoveAnymoreInVehicle(
            new CannotMoveAnymoreInVehicleNetMsg
            {
                BoatObjectId = 2,
                X = 3,
                Y = 4,
                Z = 5,
                Heading = 6
            }));
        Assert.Equal((2, 3, 4, 5, 6), (stuck.BoatObjectId, stuck.X, stuck.Y, stuck.Z, stuck.Heading));
    }
}