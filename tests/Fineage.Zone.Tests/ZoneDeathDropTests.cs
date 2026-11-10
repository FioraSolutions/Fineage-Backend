using Fineage.Game.Services;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Player.onDieDropItem — inventory onto the death tile.</summary>
public sealed class ZoneDeathDropTests
{
    [Fact]
    public async Task Npc_kill_can_drop_inventory_item()
    {
        // Force drops: 100% overall + 100% inventory item rate, limit 3.
        var rules = DeathDropRules.Parse("""
            {
              "playerDropLimit": 3,
              "playerRateDrop": 100,
              "playerRateDropItem": 100,
              "playerRateDropEquip": 0,
              "playerRateDropEquipWeapon": 0,
              "karmaDropLimit": 10,
              "karmaRateDrop": 0,
              "karmaRateDropItem": 0,
              "karmaRateDropEquip": 0,
              "karmaRateDropEquipWeapon": 0,
              "karmaPkLimit": 6,
              "nonDroppableItemIds": [57]
            }
            """);
        var kit = new NetTestKit(
            Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(),
            "elven_starting",
            deathDrop: rules,
            deathRandom: new AlwaysLow());
        var mob = kit.World.AttackableForTest()!;
        var (session, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        // Keep Level/Exp in sync — death exp loss re-derives Level from Exp, and drops need level > 4.
        player.Level = 10;
        player.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(10);
        Assert.Null(await kit.World.TryGiveItemAsync(session, 1864, 2, default));
        long before = player.Inventory!.CountOf(1864);
        Assert.True(before >= 2);

        kit.World.KillPlayerByNpcForTest(player, mob);

        Assert.True(player.Dead);
        Assert.True(player.Level > 4, $"delevel wiped drop gate level={player.Level}");
        Assert.True(player.Inventory.CountOf(1864) < before,
            $"expected stem drop, still has {player.Inventory.CountOf(1864)} ground={kit.World.GroundItemCountForTest()}");
        Assert.True(kit.World.GroundItemCountForTest() >= 1);
    }

    [Fact]
    public async Task Low_level_vs_npc_does_not_drop()
    {
        var rules = DeathDropRules.Parse("""
            {
              "playerDropLimit": 3, "playerRateDrop": 100, "playerRateDropItem": 100,
              "playerRateDropEquip": 100, "playerRateDropEquipWeapon": 100,
              "karmaDropLimit": 10, "karmaRateDrop": 100, "karmaRateDropItem": 100,
              "karmaRateDropEquip": 100, "karmaRateDropEquipWeapon": 100,
              "karmaPkLimit": 6, "nonDroppableItemIds": [57]
            }
            """);
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting",
            deathDrop: rules, deathRandom: new AlwaysLow());
        var mob = kit.World.AttackableForTest()!;
        var (session, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        player.Level = 4;
        Assert.Null(await kit.World.TryGiveItemAsync(session, 1864, 1, default));
        long before = player.Inventory!.CountOf(1864);
        kit.World.KillPlayerByNpcForTest(player, mob);
        Assert.Equal(before, player.Inventory.CountOf(1864));
        Assert.Equal(0, kit.World.GroundItemCountForTest());
    }

    /// <summary>Rnd.Next(100) always 0 → every percent check succeeds.</summary>
    sealed class AlwaysLow : Random
    {
        public override int Next(int maxValue) => 0;
    }
}
