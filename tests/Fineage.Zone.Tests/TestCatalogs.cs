using Fineage.Game;
using Fineage.Zone.Npcs;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.Tests;

/// <summary>Shared, read-only catalogs so integration tests do not each reload multi-megabyte JSON from disk.</summary>
internal static class TestCatalogs
{
    public static readonly GameData Game = GameData.LoadDefault();
    public static readonly SpawnCatalog Spawns = SpawnCatalog.LoadDefault();
    public static readonly NpcCatalog Npcs = NpcCatalog.LoadDefault();
}