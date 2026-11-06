using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>RequestItemUse</c> → <c>ItemHandler</c> for <c>handler="ItemSkills"</c> items: the item's skill list runs
/// on the player and one item leaves the stack. Covers the four shapes players meet first — a healing potion, a mana
/// potion, the Scroll of Escape, a compressed shot package — plus the reuse stamp and the refusal paths.
/// </summary>
public sealed class ConsumableUseTests
{
    const int ScrollOfEscape = 736;
    const int ManaPotion = 728;
    const int LesserHealingPotion = 1060;
    const int CompressedSoulshots = 5134;
    const int SoulshotNoGrade = 1835;
    const int Adena = 57;

    /// <summary>Attaches the recording bridge so the test can read what the client was told.</summary>
    static NetBridgeRecorder Recorder(NetTestKit kit)
    {
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return rec;
    }

    /// <summary>Gives the item to the bag and returns its object id.</summary>
    static int Grant(NetTestKit kit, ZoneEntity player, int itemId, int count = 1)
    {
        lock (kit.World)
            return player.Inventory!.Add(itemId, count).ObjectId;
    }

    [Fact]
    public async Task Mana_potion_restores_mp_and_consumes_one()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int potion = Grant(kit, p, ManaPotion);
        int before = p.Inventory!.CountOf(ManaPotion);
        p.CurMp = 0;

        Assert.Null(await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = potion }, CancellationToken.None));

        Assert.Equal(Math.Min(p.MaxMp, 1000), p.CurMp);
        Assert.Equal(before - 1, p.Inventory.CountOf(ManaPotion));
        // Mobius consumes one item: the stack survives as Modify, not Remove.
        var update = Assert.IsType<InventoryUpdateNetMsg>(
            rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate))[^1][1]);
        Assert.Equal(InventoryChange.Modify, update.Changes[0].Change);
        Assert.Equal(before - 1, update.Changes[0].Count);
    }

    [Fact]
    public async Task Healing_potion_heals_and_disappears_when_the_last_one_is_used()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int potion = Grant(kit, p, LesserHealingPotion);
        p.CurHp = 1;
        int expected = Math.Min(p.MaxHp, 1 + 8 * 3);

        Assert.Null(await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = potion }, CancellationToken.None));

        Assert.Equal(expected, p.CurHp);
        Assert.Equal(0, p.Inventory!.CountOf(LesserHealingPotion));
        var update = Assert.IsType<InventoryUpdateNetMsg>(
            rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate))[^1][1]);
        Assert.Equal(InventoryChange.Remove, update.Changes[0].Change);
        // HealOverTime 2031 is 15 s: the potion icon has to reach the client, not only the HP bar.
        Assert.Contains(p.Abnormals, a => a.Id == 2031);
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutAbnormalStatusUpdate)));
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutStatusUpdate)));
    }

    [Fact]
    public async Task Scroll_of_escape_moves_the_player_to_the_town_of_its_map_region()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int scroll = Grant(kit, p, ScrollOfEscape);
        int before = p.Inventory!.CountOf(ScrollOfEscape);
        int beforeX = p.X, beforeY = p.Y;

        Assert.Null(await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = scroll }, CancellationToken.None));

        // Elven Village sits in elf_town, whose normal respawn is 45487/49767/-2950.
        Assert.Equal(45487, p.X);
        Assert.Equal(49767, p.Y);
        Assert.Equal(-2950, p.Z);
        Assert.NotEqual((beforeX, beforeY), (p.X, p.Y));
        Assert.Equal(before - 1, p.Inventory!.CountOf(ScrollOfEscape));
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutTeleportToLocation)));
        // Mobius teleToLocation is followed by a UserInfo so the client's own pawn agrees on the new position.
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutUserInfo)));
    }

    [Fact]
    public async Task Compressed_shot_package_turns_into_shot_items()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int package = Grant(kit, p, CompressedSoulshots);
        int shotsBefore = p.Inventory!.CountOf(SoulshotNoGrade);

        Assert.Null(await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = package }, CancellationToken.None));

        Assert.Equal(0, p.Inventory.CountOf(CompressedSoulshots));
        Assert.Equal(shotsBefore + 300, p.Inventory.CountOf(SoulshotNoGrade));
        // The package's shots merge into the stack the character already carries, so the Add row reports the merged count.
        var added = rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate))
            .Select(call => Assert.IsType<InventoryUpdateNetMsg>(call[1]))
            .SelectMany(update => update.Changes)
            .Any(row => row.ItemId == SoulshotNoGrade && row.Change == InventoryChange.Add && row.Count == shotsBefore + 300);
        Assert.True(added);
    }

    [Fact]
    public async Task Reuse_delay_blocks_a_second_potion_until_it_expires()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int potion = Grant(kit, p, LesserHealingPotion, 2);
        p.CurHp = 1;

        Assert.Null(await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = potion }, CancellationToken.None));
        Assert.Equal(1, p.Inventory!.CountOf(LesserHealingPotion));

        // Mobius skill 2031 carries reuseDelay 10000 ms.
        Assert.Equal(ZoneWorld.InventoryReuse,
            await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = potion }, CancellationToken.None));
        Assert.Equal(1, p.Inventory.CountOf(LesserHealingPotion));

        kit.Now = kit.Now.AddSeconds(11);
        p.CurHp = 1;
        Assert.Null(await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = potion }, CancellationToken.None));
        Assert.Equal(0, p.Inventory.CountOf(LesserHealingPotion));
    }

    [Fact]
    public async Task Item_without_a_skill_handler_is_still_not_equippable()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int adena = Grant(kit, p, Adena, 10);

        Assert.Equal(InventoryProtocol.NotEquippable,
            await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = adena }, CancellationToken.None));
        Assert.Equal(10, p.Inventory!.CountOf(Adena));
    }

    [Fact]
    public async Task A_dead_player_cannot_drink_a_potion()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int potion = Grant(kit, p, ManaPotion);
        int before = p.Inventory!.CountOf(ManaPotion);
        p.CurMp = 0;
        p.Dead = true;

        Assert.Equal(InventoryProtocol.Dead,
            await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = potion }, CancellationToken.None));
        Assert.Equal(0, p.CurMp);
        Assert.Equal(before, p.Inventory!.CountOf(ManaPotion));
    }

    [Fact]
    public async Task Use_persists_the_shortened_stack()
    {
        var kit = new NetTestKit();
        var rec = Recorder(kit);
        var (s, p) = await kit.EnterAsync(classId: 18);
        int potion = Grant(kit, p, ManaPotion);
        int before = p.Inventory!.CountOf(ManaPotion);
        p.CurMp = 0;
        Assert.Null(await kit.World.TryUseItemAsync(s, new UseItemMsg { ObjectId = potion }, CancellationToken.None));

        await kit.World.LeaveAsync(s, CancellationToken.None);
        var saved = kit.Store.FindById(s.Account, s.ObjectId)!;
        Assert.Equal(before - 1, saved.Items!.Where(item => item.ItemId == ManaPotion).Sum(item => item.Count));
        Assert.Equal(Math.Min(p.MaxMp, 1000), saved.Mp);
    }

    [Fact]
    public void Catalog_loads_the_generated_mobius_rows()
    {
        var catalog = ItemSkillCatalog.LoadDefault();
        Assert.True(catalog.Count >= 30);

        Assert.True(catalog.TryGet(ScrollOfEscape, out var soe));
        Assert.Contains(soe.Effects, e => e.Kind == "escapeTown");

        Assert.True(catalog.TryGet(ManaPotion, out var mana));
        Assert.Contains(mana.Effects, e => e.Kind == "manaHeal" && e.Power == 1000);

        Assert.True(catalog.TryGet(CompressedSoulshots, out var package));
        Assert.Contains(package.Effects, e => e.Kind == "restoration" && e.ItemId == SoulshotNoGrade && e.Count == 300);
    }

    [Fact]
    public void Map_region_catalog_resolves_the_elven_village_cell()
    {
        var regions = MapRegionCatalog.LoadDefault();
        Assert.True(regions.Count > 10);

        var region = regions.At(NetTestKit.ElfX, NetTestKit.ElfY);
        Assert.NotNull(region);
        Assert.Equal("elf_town", region!.Name);

        var spawn = regions.TownRespawn(NetTestKit.ElfX, NetTestKit.ElfY);
        Assert.Equal(45487, spawn.X);
        Assert.Equal(49767, spawn.Y);

        // A cell outside every region falls back to Mobius' default restart, Talking Island.
        Assert.Null(regions.At(-2000000, -2000000));
        Assert.Equal(regions.Fallback.NormalSpawn()!.X, regions.TownRespawn(-2000000, -2000000).X);
    }
}
