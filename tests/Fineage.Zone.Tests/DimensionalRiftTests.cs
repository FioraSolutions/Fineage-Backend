using Fineage.Zone.DimensionalRift;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class DimensionalRiftTests
{
    static readonly DateTimeOffset Start = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_loads_rooms_and_boss_room_9()
    {
        var catalog = DimensionalRiftCatalog.LoadDefault();
        Assert.True(catalog.TryGetRoom(0, 0, out _));
        Assert.True(catalog.TryGetRoom(1, 9, out var boss));
        Assert.True(boss.Boss);
        Assert.NotEmpty(catalog.SpawnsInRoom(1, 9));
        Assert.Contains(catalog.SpawnsInRoom(1, 9), r => r.MobId == 25333);
    }

    [Fact]
    public void IsInDimensionalRift_excludes_waiting_room()
    {
        var catalog = DimensionalRiftCatalog.LoadDefault();
        var bench = new WorldBench(clock: () => Start);
        var peace = catalog.Room(0, 0);
        Assert.True(bench.World.IsInDimensionalRiftPeace(peace.Teleport.X, peace.Teleport.Y, peace.Teleport.Z));
        Assert.False(bench.World.IsInDimensionalRift(peace.Teleport.X, peace.Teleport.Y, peace.Teleport.Z));
        Assert.True(bench.World.IsInDimensionalRiftZone(-115000, -180000, -6700));
    }

    [Fact]
    public void Board_tracks_room_occupancy()
    {
        var catalog = DimensionalRiftCatalog.LoadDefault();
        var board = new DimensionalRiftBoard(catalog);
        Assert.True(board.IsRoomFree(1, 1));
        board.Occupy(1, 1, 7);
        Assert.False(board.IsRoomFree(1, 1));
        Assert.DoesNotContain(1, board.FreeRooms(1));
        board.Release(1, 1);
        Assert.True(board.IsRoomFree(1, 1));
    }
}