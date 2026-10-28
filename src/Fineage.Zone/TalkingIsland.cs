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

    /// <summary>Roxxy and Bitz, used by the Talking Island smoke quest. Not part of the gremlin spawnlist.</summary>
    public static readonly NpcInfoMsg[] Villagers =
    [
        Villager(50010, 30006, "Roxxy", SpawnX + 300, SpawnY, 0),
        Villager(50011, 30026, "Bitz", SpawnX + 300, SpawnY + 120, 16000)
    ];

    public static int NpcCount => Gremlins.Length + Villagers.Length;

    static NpcInfoMsg Villager(int objectId, int npcId, string name, int x, int y, int heading) => new()
    {
        ObjectId = objectId,
        NpcId = npcId,
        Name = name,
        X = x,
        Y = y,
        Z = SpawnZ,
        Heading = heading,
        Attackable = false,
        RunSpeed = 80f,
        WalkSpeed = 40f
    };

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
