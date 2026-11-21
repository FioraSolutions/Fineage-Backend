namespace Fineage.Zone.Simulation;

/// <summary>Square spatial cells keyed as one <see cref="long"/>, the layout <see cref="RegionContainer"/> indexes by.</summary>
public static class SpatialCells
{
    public static long Pack(int cellX, int cellY) => ((long)cellX << 32) ^ (uint)cellY;

    public static (int X, int Y) Unpack(long key) => ((int)(key >> 32), (int)(uint)key);

    public static int Of(int coordinate, int cellSize)
    {
        int quotient = coordinate / cellSize;
        if (coordinate < 0 && coordinate % cellSize != 0)
            quotient--;
        return quotient;
    }

    public static long KeyAt(int x, int y, int cellSize) => Pack(Of(x, cellSize), Of(y, cellSize));
}
