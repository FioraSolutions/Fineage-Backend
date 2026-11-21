using Fineage.Accounts;
using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius S-grade dual swords: template weapon stats, Dual Weapon Mastery (144), and equip SA via GearSkills.
/// </summary>
public sealed class ZoneSDualEquipTests
{
    const int Duelist = 88;
    const int SDualTallumDark = 6580; // Tallum Blade*Dark Legion's Edge → SA 3552 Lv.1
    const int DualWeaponMastery = 144;

    static (ZoneWorld World, ZoneEntity Player, PlayerInventory Bag) Duelist80()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity
        {
            ObjectId = 1,
            Kind = ZoneEntityKind.Player,
            ClassId = Duelist,
            Level = 80,
            Inventory = bag,
            MaxHp = 5000,
            CurHp = 5000,
            MaxMp = 2000,
            CurMp = 2000,
        };
        player.KnownSkills[DualWeaponMastery] = 37;
        var world = new ZoneWorld(game: game);
        world.PlaceForTests(player);
        world.RefreshGearSkills(player);
        world.RefreshPlayerMaxima(player, fanout: false);
        return (world, player, bag);
    }

    [Fact]
    public void S_dual_raises_pAtk_and_applies_dual_weapon_mastery()
    {
        var (world, player, bag) = Duelist80();
        int naked = world.PlayerStats(player).PAtk;

        bag.Equip(bag.Add(SDualTallumDark));
        world.RefreshGearSkills(player);
        int withDual = world.PlayerStats(player).PAtk;

        Assert.True(withDual > naked, $"P.Atk with S dual {withDual} should exceed naked {naked}");
        Assert.Contains(3552, player.GearSkills.Keys);
        Assert.Equal(WeaponType.Dual, world.PlayerStats(player).Weapon);

        bag.Unequip(bag.GetEquipped(PaperdollSlot.RHand)!);
        world.RefreshGearSkills(player);
        Assert.Equal(naked, world.PlayerStats(player).PAtk);
        Assert.DoesNotContain(3552, player.GearSkills.Keys);
    }

    [Fact]
    public void Health_sa_s_dual_style_weapon_raises_max_hp_via_3013()
    {
        var game = GameData.LoadDefault();
        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(3013, out _), "Mobius SA Health (3013) must be in skill-passives.json");

        var (world, player, bag) = Duelist80();
        world.RefreshPlayerMaxima(player, fanout: false);
        int maxHpBare = player.MaxHp;

        bag.Equip(bag.Add(6582)); // Forgotten Blade - Health → 3013 Lv.3 (+25% maxHp)
        world.RefreshGearSkills(player);
        world.RefreshPlayerMaxima(player, fanout: false);

        Assert.Contains(3013, player.GearSkills.Keys);
        Assert.Equal(3, player.GearSkills[3013]);
        Assert.True(player.MaxHp > maxHpBare, $"MaxHp {player.MaxHp} should exceed bare {maxHpBare}");
    }

    [Fact]
    public async Task Equipping_s_dual_fans_out_UserInfo_with_higher_pAtk()
    {
        var bench = new WorldBench();
        BenchPlayer actor = await bench.EnterAsync(
            "dualist",
            "Duel",
            [new StoredItem(SDualTallumDark, 1, -1)],
            classId: Duelist,
            level: 80);
        var player = actor.Player;
        player.KnownSkills[DualWeaponMastery] = 37;

        int nakedPatk = bench.World.BuildUserInfo(player).PAtk;
        bench.Rec.Calls.Clear();

        Assert.Null(await actor.Use(actor.Slot(SDualTallumDark)));

        var ui = Assert.IsType<UserInfoMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutUserInfo)).Last()[1]);
        Assert.True(ui.PAtk > nakedPatk, $"UserInfo P.Atk {ui.PAtk} should exceed pre-equip {nakedPatk}");
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutUserInfo)));
    }
}