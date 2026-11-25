using Fineage.Net.Contract;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NetBoatVehicleWireTests
{
    const int BoatSpawnX = 48950;
    const int BoatSpawnY = 190613;
    const int BoatSpawnZ = -3610;

    [Fact]
    public async Task Boat_spawn_and_departure_fanout_vehicle_packets()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var boat = Assert.Single(kit.World.BoatsForTests(), b =>
            b.X == BoatSpawnX && b.Y == BoatSpawnY && b.Z == BoatSpawnZ);
        var (_, player) = await kit.EnterAsync(x: BoatSpawnX, y: BoatSpawnY, z: BoatSpawnZ);

        rec.Calls.Clear();
        kit.World.BroadcastBoatMoveForTests(boat, boat.X, boat.Y, boat.Z);
        var infoArgs = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutVehicleInfo)));
        var info = (VehicleInfoNetMsg)infoArgs[1]!;
        Assert.Equal(boat.ObjectId, info.BoatObjectId);
        Assert.Equal(boat.X, info.X);
        Assert.Equal(boat.Heading, info.Heading);
        Assert.Equal([player.ObjectId], (IReadOnlyList<int>)infoArgs[0]!);

        rec.Calls.Clear();
        boat.MoveActive = true;
        boat.Running = true;
        boat.RunSpeed = 800;
        kit.World.BroadcastBoatMoveForTests(boat, boat.X + 500, boat.Y, boat.Z);
        var depArgs = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutVehicleDeparture)));
        Assert.Equal([player.ObjectId], (IReadOnlyList<int>)depArgs[0]!);
        var departure = (VehicleDepartureNetMsg)depArgs[1]!;
        Assert.Equal(boat.ObjectId, departure.BoatObjectId);
        Assert.Equal(800, departure.MoveSpeed);
        Assert.Equal(ZoneWorld.DefaultBoatRotationSpeed, departure.RotationSpeed);
        Assert.Equal(boat.X + 500, departure.DestX);
    }
}