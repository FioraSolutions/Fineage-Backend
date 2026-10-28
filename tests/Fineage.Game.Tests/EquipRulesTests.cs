using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class EquipRulesTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(19, 0)]
    [InlineData(20, 1)]
    [InlineData(39, 1)]
    [InlineData(40, 2)]
    [InlineData(51, 2)]
    [InlineData(52, 3)]
    [InlineData(60, 3)]
    [InlineData(61, 4)]
    [InlineData(75, 4)]
    [InlineData(76, 5)]
    [InlineData(80, 5)]
    public void Expertise_rank_follows_the_auto_get_levels(int level, int rank)
    {
        Assert.Equal(rank, Expertise.ForLevel(level));
        Assert.Equal(rank, new EquipActor(0, 0, 0, level).ExpertiseLevel);
    }

    [Fact]
    public void Grade_penalty_uses_the_highest_crystal_above_expertise_and_caps_at_four()
    {
        var inventory = new PlayerInventory(new ItemCatalog(
        [
            Weapon(1, CrystalGrade.D),
            Armor(2, CrystalGrade.C),
            Weapon(3, CrystalGrade.S),
            Arrow(4, CrystalGrade.S)
        ]));
        inventory.Equip(inventory.Add(1));
        inventory.Equip(inventory.Add(2));

        var both = GradePenalty.Compute(inventory.Items, expertiseLevel: 0, bonus: 0, enabled: true);
        Assert.Equal(1, both.Weapon);
        Assert.Equal(2, both.Armor);
        Assert.Equal(2, both.SkillLevel);

        inventory.Equip(inventory.Add(3));
        var capped = GradePenalty.Compute(inventory.Items, expertiseLevel: 0, bonus: 0, enabled: true);
        Assert.Equal(GradePenalty.MaxLevel, capped.Weapon);
        Assert.Equal(4, capped.SkillLevel);

        var within = GradePenalty.Compute(inventory.Items, expertiseLevel: 5, bonus: 0, enabled: true);
        Assert.Equal(0, within.Weapon);
        Assert.Equal(0, within.Armor);

        var bonus = GradePenalty.Compute(inventory.Items, expertiseLevel: 0, bonus: 3, enabled: true);
        Assert.Equal(2, bonus.Weapon);
        Assert.Equal(0, bonus.Armor);

        Assert.Equal(default, GradePenalty.Compute(inventory.Items, 0, 0, enabled: false));

        var arrows = new PlayerInventory(new ItemCatalog([Weapon(10, CrystalGrade.None, WeaponType.Bow, BodySlot.LrHand), Arrow(4, CrystalGrade.S)]));
        var bow = arrows.Add(10);
        var stack = arrows.Add(4, 10);
        arrows.Equip(bow);
        arrows.Equip(stack);
        var ignored = GradePenalty.Compute(arrows.Items, 0, 0, enabled: true);
        Assert.Equal(0, ignored.Weapon);
        Assert.Equal(0, ignored.Armor);
    }

    [Fact]
    public void Conditions_reject_the_matching_reason_and_allow_a_match()
    {
        var human = new EquipActor(0, 0, 0, 40);
        var races = new EquipRequirement.Races([0, 1, 2, 3, 4]);
        Assert.Null(races.Deny(human));
        Assert.Equal(EquipDeny.Race, races.Deny(human with { Race = 99 }));

        var classes = new EquipRequirement.Classes([5, 6]);
        Assert.Equal(EquipDeny.Class, classes.Deny(human));
        Assert.Null(classes.Deny(human with { ClassId = 6 }));

        Assert.Equal(EquipDeny.Sex, new EquipRequirement.SexIs(1).Deny(human));
        Assert.Null(new EquipRequirement.SexIs(0).Deny(human));
        Assert.Equal(EquipDeny.Hero, new EquipRequirement.HeroIs(true).Deny(human));
        Assert.Null(new EquipRequirement.HeroIs(true).Deny(human with { Hero = true }));
        Assert.Equal(EquipDeny.Pvp, new EquipRequirement.PvpIs(false).Deny(human with { PvpFlagged = true }));
        Assert.Null(new EquipRequirement.PvpIs(true).Deny(human with { PvpFlagged = true }));
        Assert.Equal(EquipDeny.Level, new EquipRequirement.MinLevel(46).Deny(human));
        Assert.Null(new EquipRequirement.MinLevel(40).Deny(human));
        Assert.Equal(EquipDeny.Pk, new EquipRequirement.PkAtMost(0).Deny(human with { PkKills = 1 }));
        Assert.Equal(EquipDeny.Grade, new EquipRequirement.ExpertiseIs(1).Deny(human));
        Assert.Null(new EquipRequirement.ExpertiseIs(2).Deny(human));
    }

    [Fact]
    public void Pledge_castle_and_clan_hall_follow_the_clan_checks()
    {
        var lone = new EquipActor(0, 0, 0, 40);
        Assert.Equal(EquipDeny.Pledge, new EquipRequirement.PledgeAtLeast(1).Deny(lone));
        Assert.Equal(EquipDeny.ClanLeader, new EquipRequirement.PledgeAtLeast(-1).Deny(lone));
        Assert.Equal(EquipDeny.Castle, new EquipRequirement.CastleIs(6).Deny(lone));
        Assert.Null(new EquipRequirement.CastleIs(0).Deny(lone));

        var member = lone with { InClan = true, PledgeClass = 1 };
        Assert.Null(new EquipRequirement.PledgeAtLeast(1).Deny(member));
        Assert.Equal(EquipDeny.Pledge, new EquipRequirement.PledgeAtLeast(4).Deny(member));
        Assert.Null(new EquipRequirement.PledgeAtLeast(4).Deny(member with { ClanLeader = true }));
        Assert.Equal(EquipDeny.ClanLeader, new EquipRequirement.PledgeAtLeast(-1).Deny(member));
        Assert.Null(new EquipRequirement.PledgeAtLeast(-1).Deny(member with { ClanLeader = true }));

        var lord = member with { ClanLeader = true, CastleId = 6, HideoutId = 3 };
        Assert.Null(new EquipRequirement.CastleIs(-1).Deny(lord));
        Assert.Null(new EquipRequirement.CastleIs(6).Deny(lord));
        Assert.Equal(EquipDeny.Castle, new EquipRequirement.CastleIs(2).Deny(lord));
        Assert.Equal(EquipDeny.Castle, new EquipRequirement.CastleIs(-1).Deny(member));
        Assert.Null(new EquipRequirement.ClanHalls([-1]).Deny(lord));
        Assert.Equal(EquipDeny.ClanHall, new EquipRequirement.ClanHalls([-1]).Deny(member));
        Assert.Null(new EquipRequirement.ClanHalls([0]).Deny(lone));
        Assert.Null(new EquipRequirement.ClanHalls([3]).Deny(lord));

        var both = new EquipRequirement.AllOf(
        [
            new EquipRequirement.CastleIs(6),
            new EquipRequirement.PledgeAtLeast(2)
        ]);
        Assert.Equal(EquipDeny.Castle, both.Deny(lone));
        Assert.Equal(EquipDeny.Pledge, both.Deny(lord with { PledgeClass = 1, ClanLeader = false }));
        Assert.Null(both.Deny(lord with { PledgeClass = 2 }));
    }

    [Fact]
    public void Shipped_catalog_carries_the_mobius_conditions()
    {
        var items = GameData.LoadDefault().Items;
        var human = new EquipActor(HumanClass, 0, 0, 1);
        Assert.Null(items.Get(425).Equip!.Deny(human));
        Assert.Equal(EquipDeny.Race, items.Get(425).Equip!.Deny(human with { Race = 99 }));
        Assert.Equal(EquipDeny.Race, items.Get(461).Equip!.Deny(human with { Race = 99 }));
        Assert.Null(items.Get(2369).Equip);
        Assert.Equal(CrystalGrade.None, items.Get(2369).Crystal);

        Assert.Equal(CrystalGrade.S, items.Get(6611).Crystal);
        Assert.Equal(EquipDeny.Hero, items.Get(6611).Equip!.Deny(human));
        Assert.Null(items.Get(6611).Equip!.Deny(human with { Hero = true }));

        Assert.Equal(EquipDeny.Sex, items.Get(7682).Equip!.Deny(human));
        Assert.Null(items.Get(7682).Equip!.Deny(human with { Sex = 1 }));
        Assert.Null(items.Get(8923).Equip!.Deny(human));
        Assert.Equal(EquipDeny.Sex, items.Get(8923).Equip!.Deny(human with { Sex = 1 }));

        Assert.Equal(EquipDeny.Pledge, items.Get(7850).Equip!.Deny(human));
        Assert.Null(items.Get(7850).Equip!.Deny(human with { InClan = true, PledgeClass = 1 }));
        Assert.Equal(CrystalGrade.D, items.Get(7850).Crystal);
        Assert.Equal(EquipDeny.Pledge, items.Get(7860).Equip!.Deny(human with { InClan = true, PledgeClass = 1 }));
        Assert.Null(items.Get(7860).Equip!.Deny(human with { InClan = true, PledgeClass = 4 }));
        Assert.Equal(CrystalGrade.A, items.Get(7860).Crystal);

        Assert.Equal(EquipDeny.Castle, items.Get(6834).Equip!.Deny(human));
        Assert.Equal(EquipDeny.Pledge, items.Get(6834).Equip!.Deny(human with { InClan = true, CastleId = 6, PledgeClass = 1 }));
        Assert.Null(items.Get(6834).Equip!.Deny(human with { InClan = true, CastleId = 6, PledgeClass = 2 }));
        Assert.Equal(EquipDeny.Castle, items.Get(6841).Equip!.Deny(human with { InClan = true, ClanLeader = true }));
        Assert.Equal(EquipDeny.ClanLeader, items.Get(6841).Equip!.Deny(human with { InClan = true, CastleId = 1 }));
        Assert.Null(items.Get(6841).Equip!.Deny(human with { InClan = true, ClanLeader = true, CastleId = 1 }));
    }

    [Fact]
    public void Load_rejects_an_unknown_equip_field_and_crystal()
    {
        var root = Path.Combine(Path.GetTempPath(), "fineage-equip-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "starter-kits.json"), """{ "kits": [] }""");
            File.WriteAllText(Path.Combine(root, "items.json"), """
                { "items": [ { "id": 1, "name": "Blade", "kind": "weapon", "bodySlot": "rhand", "weaponType": "sword", "icon": "icon.a", "equip": { "klass": [1] } } ] }
                """);
            var error = Assert.Throws<InvalidDataException>(() => GameData.Load(root));
            Assert.Contains("klass", error.Message);

            File.WriteAllText(Path.Combine(root, "items.json"), """
                { "items": [ { "id": 1, "name": "Blade", "kind": "weapon", "bodySlot": "rhand", "weaponType": "sword", "icon": "icon.a", "crystal": "s80" } ] }
                """);
            error = Assert.Throws<InvalidDataException>(() => GameData.Load(root));
            Assert.Contains("s80", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    const int HumanClass = 0;

    static ItemTemplate Weapon(int id, CrystalGrade crystal, WeaponType type = WeaponType.Sword, BodySlot slot = BodySlot.RightHand) =>
        new(id, "Weapon " + id, ItemKind.Weapon, slot, "icon.test", type, Crystal: crystal);

    static ItemTemplate Armor(int id, CrystalGrade crystal) =>
        new(id, "Armor " + id, ItemKind.Armor, BodySlot.Chest, "icon.test", ArmorType: ArmorType.Heavy, Crystal: crystal);

    static ItemTemplate Arrow(int id, CrystalGrade crystal) =>
        new(id, "Arrow", ItemKind.Etc, BodySlot.LeftHand, "icon.test", EtcType: EtcItemType.Arrow, Stackable: true, Crystal: crystal);
}
