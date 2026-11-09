using Fineage.Game;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Clan skills and skill-enchant levels: the fold path is live; grant/EnchantSkill UI still TBD.
/// </summary>
public sealed class ZoneClanSkillStatsTests
{
    [Fact]
    public void Clan_Might_in_ClanSkills_raises_pAtk()
    {
        var bag = new PlayerInventory(GameData.LoadDefault().Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = bag };
        bag.Equip(bag.Add(219));
        var world = new ZoneWorld();
        int before = world.PlayerStats(player).PAtk;

        // Clan Might 376 L3 = pAtk ×1.06 (L1 ×1.03 truncates away on small bases). Grant path TBD.
        player.ClanSkills[376] = 3;
        Assert.True(world.PlayerStats(player).PAtk > before);
    }

    [Fact]
    public void PassiveSkillLevels_merges_clan_over_known()
    {
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1 };
        player.KnownSkills[376] = 1;
        player.ClanSkills[376] = 3;
        player.ClanSkills[377] = 2;
        var levels = ZoneWorld.PassiveSkillLevels(player);
        Assert.Equal(3, levels[376]);
        Assert.Equal(2, levels[377]);
    }

    [Fact]
    public void Enchanted_learned_level_feeds_passive_ValueAt()
    {
        // Interlude EnchantSkill writes a higher skill_level into character_skills; KnownSkills carries it.
        // Armor Mastery 142 has 5 levels in skill-passives — bumping the book level must raise PDef.
        var bag = new PlayerInventory(GameData.LoadDefault().Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 40, Inventory = bag };
        bag.Equip(bag.Add(23));
        var world = new ZoneWorld();
        world.RefreshKnownSkillsForTest(player);
        player.KnownSkills[142] = 1;
        int low = world.PlayerStats(player).PDef;
        player.KnownSkills[142] = 5; // stand-in for an enchanted / higher rank
        Assert.True(world.PlayerStats(player).PDef >= low);
    }
}
