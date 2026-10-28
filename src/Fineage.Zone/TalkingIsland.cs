using Fineage.Protocol;
using Fineage.Zone.Spawns;

namespace Fineage.Zone;

/// <summary>
/// Cedric's Training Hall. Gremlins stand here and do not move.
/// Players with no saved position use <see cref="ClassSpawn"/>.
/// Gremlin layout is loaded from <c>data/spawns/ti_cedric_training_hall.json</c>.
/// </summary>
public static class TalkingIsland
{
    public const int SpawnX = TalkingIslandSpawn.X;
    public const int SpawnY = TalkingIslandSpawn.Y;
    public const int SpawnZ = TalkingIslandSpawn.Z;
    public const int GremlinNpcId = 20001;
    public const int GremlinMaxHp = 96;
    public const int MaxStep = 8000;

    public static readonly NpcInfoMsg[] Gremlins = LoadGremlins();

    public static bool WithinStep(int fromX, int fromY, int toX, int toY)
    {
        long dx = fromX - (long)toX;
        long dy = fromY - (long)toY;
        return dx * dx + dy * dy <= (long)MaxStep * MaxStep;
    }

    static NpcInfoMsg[] LoadGremlins()
    {
        var map = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.DefaultMapId);
        var gremlins = new List<NpcInfoMsg>();
        foreach (var row in map.Npcs)
        {
            if (row.NpcId != GremlinNpcId)
                continue;
            gremlins.Add(new NpcInfoMsg
            {
                ObjectId = row.ObjectId,
                NpcId = row.NpcId,
                Name = row.Name,
                X = row.X,
                Y = row.Y,
                Z = row.Z,
                Heading = row.Heading,
                Attackable = row.Attackable,
                RunSpeed = row.RunSpeed,
                WalkSpeed = row.WalkSpeed
            });
        }

        gremlins.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
        if (gremlins.Count == 0)
            throw new InvalidOperationException("Spawn datapack has no Cedric gremlins.");
        return gremlins.ToArray();
    }
}
