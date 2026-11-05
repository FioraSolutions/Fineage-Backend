namespace Fineage.Protocol;

/// <summary>
/// Shared spawn-point type. The coordinates live in the class datapack
/// (<c>data/classes.json</c>), transcribed from L2J Mobius Interlude creation points.
/// Human fighter and mystic class ids for call sites that still name the TI MVP defaults.
/// </summary>
public static class ClassSpawn
{
    public const int HumanFighter = 0;
    public const int HumanMystic = 10;

    public readonly record struct Point(int X, int Y, int Z);

    public static Point Pick(IReadOnlyList<Point> points, Random random)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(random);
        if (points.Count == 0)
            throw new ArgumentException("Spawn list is empty.", nameof(points));
        return points[random.Next(points.Count)];
    }
}
