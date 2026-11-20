namespace Fineage.Zone.Simulation;

/// <summary>Maps a world position to the region that owns it.</summary>
public interface IRegionPartition
{
    RegionId RegionOf(int instanceId, int x, int y);
}

/// <summary>
/// Square cells of <see cref="CellSize"/> world units. <see cref="MapTiles"/> uses the Interlude map tile
/// (32768 units) with the geodata index offsets, so a region reads like its geodata file name (Giran = <c>22_22</c>).
/// </summary>
public sealed class GridRegionPartition : IRegionPartition
{
    public const int MapTileSize = 32768;
    public const int MapTileOffsetX = 20;
    public const int MapTileOffsetY = 18;

    public static GridRegionPartition MapTiles { get; } = new(MapTileSize, MapTileOffsetX, MapTileOffsetY);

    readonly int _offsetX;
    readonly int _offsetY;

    public GridRegionPartition(int cellSize, int offsetX = 0, int offsetY = 0)
    {
        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Region cell size must be positive.");
        CellSize = cellSize;
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    public int CellSize { get; }

    public RegionId RegionOf(int instanceId, int x, int y) =>
        new(instanceId, FloorDiv(x, CellSize) + _offsetX, FloorDiv(y, CellSize) + _offsetY);

    static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        if (value < 0 && value % divisor != 0)
            quotient--;
        return quotient;
    }
}
