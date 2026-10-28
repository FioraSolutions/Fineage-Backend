using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class GameDataLoadTests
{
    [Fact]
    public void Load_rejects_a_kit_that_equips_an_unknown_or_bag_item()
    {
        var root = NewDataDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "items.json"), """
                { "items": [ { "id": 57, "name": "Adena", "kind": "etc", "stackable": true, "icon": "icon.etc_adena_i00" } ] }
                """);
            File.WriteAllText(Path.Combine(root, "starter-kits.json"), """
                { "kits": [ { "classId": 0, "items": [ { "itemId": 57, "equipped": true } ] } ] }
                """);

            var error = Assert.Throws<InvalidDataException>(() => GameData.Load(root));
            Assert.Contains("57", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_rejects_duplicate_item_ids()
    {
        var root = NewDataDir();
        try
        {
            File.WriteAllText(Path.Combine(root, "items.json"), """
                {
                  "items": [
                    { "id": 10, "name": "Dagger", "kind": "weapon", "bodySlot": "rhand", "weaponType": "dagger", "icon": "icon.a" },
                    { "id": 10, "name": "Dagger", "kind": "weapon", "bodySlot": "rhand", "weaponType": "dagger", "icon": "icon.a" }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(root, "starter-kits.json"), """{ "kits": [] }""");

            var error = Assert.Throws<InvalidDataException>(() => GameData.Load(root));
            Assert.Contains("10", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string NewDataDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "fineage-game-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
