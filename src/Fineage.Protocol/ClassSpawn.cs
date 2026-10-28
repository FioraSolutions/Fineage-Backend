namespace Fineage.Protocol;

/// <summary>
/// Talking Island creation points for a new character with no saved position.
/// Coordinates were checked against L2J Mobius Interlude StartingClass templates
/// (human fighter class 0, human mystic class 10) and written here. This is not that XML.
/// </summary>
public static class ClassSpawn
{
    public const int HumanFighter = 0;
    public const int HumanMystic = 10;

    public readonly record struct Point(int X, int Y, int Z);

    public static readonly Point[] HumanFighterPoints =
    [
        new(-71338, 258271, -3104),
        new(-71417, 258270, -3104),
        new(-71453, 258305, -3104),
        new(-71467, 258378, -3104)
    ];

    public static readonly Point[] HumanMysticPoints =
    [
        new(-90875, 248162, -3570),
        new(-90954, 248118, -3570),
        new(-90918, 248070, -3570),
        new(-90890, 248027, -3570)
    ];

    public static Point[] Points(int classId) => classId switch
    {
        HumanMystic => HumanMysticPoints,
        HumanFighter => HumanFighterPoints,
        _ => [new Point(TalkingIslandSpawn.X, TalkingIslandSpawn.Y, TalkingIslandSpawn.Z)]
    };

    /// <summary>Stable point for the login roster. The zone picks among <see cref="Points"/> until a position is saved.</summary>
    public static Point Preview(int classId) => Points(classId)[0];

    public static Point Pick(int classId, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var points = Points(classId);
        return points[random.Next(points.Length)];
    }
}
