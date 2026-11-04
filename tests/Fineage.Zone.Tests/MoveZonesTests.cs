using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class MoveZonesTests
{
    static DoorTable OneDoor(bool open) => new([
        new DoorInstance
        {
            Id = 1, Name = "gate", X = 100, Y = 0, Z = 0,
            Nodes = [(95, -50), (105, -50), (105, 50), (95, 50)],
            MinZ = 0, MaxZ = 150, Open = open,
            MinX = 95, MaxX = 105, MinY = -50, MaxY = 50
        }
    ]);

    [Fact]
    public void Closed_door_clips_a_crossing_segment_before_the_door()
    {
        var door = OneDoor(open: false).FirstBlocking(0, 0, 0, 200, 0, 0, out int x, out int y, out _);
        Assert.NotNull(door);
        Assert.InRange(x, 60, 94);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Open_door_or_clear_path_does_not_block()
    {
        Assert.Null(OneDoor(open: true).FirstBlocking(0, 0, 0, 200, 0, 0, out _, out _, out _));
        Assert.Null(OneDoor(open: false).FirstBlocking(0, 100, 0, 200, 100, 0, out _, out _, out _));
        // Far above the door span (another floor).
        Assert.Null(OneDoor(open: false).FirstBlocking(0, 0, 1000, 200, 0, 1000, out _, out _, out _));
    }

    [Fact]
    public void Water_cuboid_uses_xy_and_z_span()
    {
        var w = new WaterZones([(0, 100, 0, 100, -500, -100)]);
        Assert.True(w.IsInWater(50, 50, -200));
        Assert.False(w.IsInWater(50, 50, 0));
        Assert.False(w.IsInWater(150, 50, -200));
    }

    [Fact]
    public void Datapack_files_load()
    {
        Assert.True(DoorTable.LoadDefault().Count > 500);
        Assert.True(WaterZones.LoadDefault().Count > 400);
    }
}
