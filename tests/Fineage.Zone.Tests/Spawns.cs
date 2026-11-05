using Fineage.Game;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.Tests;

/// <summary>
/// Spawn catalogs for benches and Net hosts. A world with no catalog spawns no NPC, so any test that talks to a
/// merchant, trainer, or quest giver has to hand one in.
/// </summary>
internal static class Spawns
{
    /// <summary>Where <see cref="WorldBench"/> puts a human, so a test merchant can stand in talk range.</summary>
    public const int HumanStartX = 46045;
    public const int HumanStartY = 41251;
    public const int HumanStartZ = -3440;

    /// <summary>A one-merchant map. The merchant's town and tax come from the map id, as in production.</summary>
    public static SpawnCatalog Merchant(
        int npcObjectId,
        int npcId,
        int x = HumanStartX,
        int y = HumanStartY,
        int z = HumanStartZ,
        string npcType = "Merchant",
        string title = "Trader",
        string mapId = "shop_flat")
    {
        var npc = new SpawnNpcDefinition(
            npcObjectId, npcId, "Lector", x, y, z, 0, false, 100, 80f, 40f, 60, 0, title, npcType);
        var map = new SpawnMapDefinition(mapId, true, [npc]);
        return SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [mapId] = map });
    }

    public static GameData Kit(GameData game, int classId, params StarterKitEntry[] entries) =>
        GameData.From(
            game.Items,
            new StarterKits([new StarterKit(classId, entries)]));
}

/// <summary>A clock a test can move by hand, for restock windows and timers.</summary>
internal sealed class MutableClock
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
}
