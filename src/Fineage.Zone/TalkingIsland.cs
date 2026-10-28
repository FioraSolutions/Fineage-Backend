using Fineage.Protocol;

namespace Fineage.Zone;

/// <summary>
/// Cedric's Training Hall. Gremlins stand here and do not move.
/// Players with no saved position use <see cref="ClassSpawn"/>.
/// </summary>
public static class TalkingIsland
{
    public const int SpawnX = TalkingIslandSpawn.X;
    public const int SpawnY = TalkingIslandSpawn.Y;
    public const int SpawnZ = TalkingIslandSpawn.Z;
    public const int GremlinNpcId = 20001;
    public const int GremlinMaxHp = 96;
    /// <summary>Minimal loot after a gremlin kill (stackable adena).</summary>
    public const int GremlinDropItemId = 57;
    public const int GremlinDropCount = 25;
    public const int MaxStep = 8000;

    public static readonly NpcInfoMsg[] Gremlins =
    [
        Gremlin(50001, SpawnX + 150, SpawnY + 80, 16000),
        Gremlin(50002, SpawnX - 220, SpawnY + 60, 0),
        Gremlin(50003, SpawnX + 40, SpawnY - 180, 32000)
    ];

    public static bool WithinStep(int fromX, int fromY, int toX, int toY)
    {
        long dx = fromX - (long)toX;
        long dy = fromY - (long)toY;
        return dx * dx + dy * dy <= (long)MaxStep * MaxStep;
    }

    static NpcInfoMsg Gremlin(int objectId, int x, int y, int heading) => new()
    {
        ObjectId = objectId,
        NpcId = GremlinNpcId,
        Name = "Gremlin",
        X = x,
        Y = y,
        Z = SpawnZ,
        Heading = heading,
        Attackable = true,
        RunSpeed = 80f,
        WalkSpeed = 40f
    };
}
