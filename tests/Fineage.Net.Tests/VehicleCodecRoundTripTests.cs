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
    public void Vehicle_opcodes_map_to_interlude()
    {
        Assert.Equal(0x03E1, NetOpcode.VehicleInfo);
        Assert.Equal(0x03E2, NetOpcode.VehicleDeparture);
        Assert.NotEqual(NetOpcode.RequestEnchantSkillInfo, NetOpcode.VehicleInfo);
        Assert.NotEqual(NetOpcode.RequestEnchantSkill, NetOpcode.VehicleDeparture);
    }
}