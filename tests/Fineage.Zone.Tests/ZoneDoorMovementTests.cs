using Fineage.Zone.Geo;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// A closed door must be part of the shared geo path search, not a clip applied after the route is chosen.
/// Mobius <c>GeoEngine.getValidLocation</c> calls <c>DoorData.checkIfDoorsBetween</c> before walking cells, so
/// <c>Creature.moveToLocation</c> follows the same pathfinding branch as a wall; the per-tick
/// <c>checkIfDoorsBetween</c> in <c>updatePosition</c> remains the final safety net.
/// </summary>
public sealed class ZoneDoorMovementTests
{
    static GeoEngine FlatElfGround()
    {
        var geo = new GeoEngine();
        geo.SetRegion(21, 19, GeoEngine.Region.Flat1((short)NetTestKit.ElfZ));
        return geo;
    }

    static DoorTable DoorAt(int x, int y, int z, bool open = false) => new([
        new DoorInstance
        {
            Id = 90_001, Name = "test_gate", X = x, Y = y, Z = z,
            Nodes = [(x - 50, y - 50), (x + 50, y - 50), (x + 50, y + 50), (x - 50, y + 50)],
            MinZ = z, MaxZ = z + 150, Open = open,
            MinX = x - 50, MaxX = x + 50, MinY = y - 50, MaxY = y + 50
        }
    ]);

    [Fact]
    public async Task Closed_door_on_the_line_queues_a_route_instead_of_stopping_at_the_clip()
    {
        var bench = new WorldBench(geo: FlatElfGround());
        bench.World.Doors = DoorAt(NetTestKit.ElfX + 100, NetTestKit.ElfY, NetTestKit.ElfZ);
        var p = await bench.EnterAsync(classId: 0, race: 0, x: NetTestKit.ElfX, y: NetTestKit.ElfY, z: NetTestKit.ElfZ);

        Assert.Null(await bench.World.TryMoveAsync(
            p.Session, NetTestKit.ElfX + 300, NetTestKit.ElfY, NetTestKit.ElfZ, CancellationToken.None));

        // Before the fix MoveCheck ignored the door, ClipDoors shortened the leg, and no route was asked for.
        Assert.True(p.Player.PathfindPending, "a closed door must take the blocked-move path-search branch");
        Assert.False(p.Player.MoveActive);

        bench.World.RunStepForTest();

        Assert.False(p.Player.PathfindPending);
        Assert.True(p.Player.MoveActive || p.Player.MovePath.Count > 0, "the resolved route must start the pawn");
    }

    [Fact]
    public async Task Open_door_on_the_line_keeps_the_direct_move()
    {
        var bench = new WorldBench(geo: FlatElfGround());
        bench.World.Doors = DoorAt(NetTestKit.ElfX + 100, NetTestKit.ElfY, NetTestKit.ElfZ, open: true);
        var p = await bench.EnterAsync(classId: 0, race: 0, x: NetTestKit.ElfX, y: NetTestKit.ElfY, z: NetTestKit.ElfZ);

        Assert.Null(await bench.World.TryMoveAsync(
            p.Session, NetTestKit.ElfX + 300, NetTestKit.ElfY, NetTestKit.ElfZ, CancellationToken.None));

        Assert.False(p.Player.PathfindPending);
        Assert.True(p.Player.MoveActive);
        Assert.Equal(NetTestKit.ElfX + 300, p.Player.MoveDestX);
    }
}
