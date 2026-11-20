namespace Fineage.Zone.Simulation;

/// <summary>Maps a world position to the region that owns it.</summary>
public interface IRegionPartition
{
    /// <summary>The region an entity placed at this position belongs to.</summary>
    RegionId RegionOf(int instanceId, int x, int y);

    /// <summary>
    /// Whether <paramref name="region"/> keeps owning an entity now at this position: the same instance and inside the
    /// region widened by the handoff hysteresis, so pacing along an edge does not hand the entity back and forth.
    /// </summary>
    bool Holds(RegionId region, int instanceId, int x, int y);
}

/// <summary>
/// Square cells of <see cref="CellSize"/> world units. <see cref="MapTiles"/> uses the Interlude map tile
/// (32768 units) with the geodata index offsets, so a region reads like its geodata file name (Giran = <c>22_22</c>),
/// and hands an entity off <see cref="MapTileHandoffMargin"/> units past the tile edge.
/// </summary>
public sealed class GridRegionPartition : IRegionPartition
{
    public const int MapTileSize = 32768;
    public const int MapTileOffsetX = 20;
    public const int MapTileOffsetY = 18;
    public const int MapTileHandoffMargin = 300;

    public static GridRegionPartition MapTiles { get; } =
        new(MapTileSize, MapTileOffsetX, MapTileOffsetY, MapTileHandoffMargin);

    readonly int _offsetX;
    readonly int _offsetY;

    /// <param name="handoffMargin">Units past the cell edge an owner keeps an entity; below half the cell size.</param>
    public GridRegionPartition(int cellSize, int offsetX = 0, int offsetY = 0, int handoffMargin = 0)
    {
        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Region cell size must be positive.");
        if (handoffMargin < 0 || handoffMargin * 2 >= cellSize)
            throw new ArgumentOutOfRangeException(nameof(handoffMargin), handoffMargin,
                $"Handoff margin must be at least 0 and below half the {cellSize}-unit cell.");
        CellSize = cellSize;
        HandoffMargin = handoffMargin;
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    public int CellSize { get; }

    public int HandoffMargin { get; }

    public RegionId RegionOf(int instanceId, int x, int y) =>
        new(instanceId, FloorDiv(x, CellSize) + _offsetX, FloorDiv(y, CellSize) + _offsetY);

    public bool Holds(RegionId region, int instanceId, int x, int y)
    {
        if (region.InstanceId != instanceId)
            return false;
        long minX = (long)(region.X - _offsetX) * CellSize - HandoffMargin;
        long minY = (long)(region.Y - _offsetY) * CellSize - HandoffMargin;
        long span = CellSize + 2L * HandoffMargin;
        return x >= minX && x < minX + span && y >= minY && y < minY + span;
    }

    static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        if (value < 0 && value % divisor != 0)
            quotient--;
        return quotient;
    }
}
