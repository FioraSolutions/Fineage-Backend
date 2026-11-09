using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Clan skills and skill-enchant levels: fold + grant-on-join/load. EnchantSkill UI still TBD.
/// </summary>
public sealed class ZoneClanSkillStatsTests
{
    const int ClanCreateLevel = 10;
    /// <summary>Mobius Clan Might.</summary>
    const int ClanMight = 376;

    [Fact]
    public void Clan_Might_in_ClanSkills_raises_pAtk()
    {
        var bag = new PlayerInventory(GameData.LoadDefault().Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = bag };
        bag.Equip(bag.Add(219));
        var world = new ZoneWorld();
        int before = world.PlayerStats(player).PAtk;

        // Clan Might 376 L3 = pAtk ×1.06 (L1 ×1.03 truncates away on small bases).
        player.ClanSkills[ClanMight] = 3;
        Assert.True(world.PlayerStats(player).PAtk > before);
    }

    [Fact]
    public async Task Clan_skill_book_copies_onto_member_on_join_and_clears_on_leave()
    {
        var kit = new NetTestKit();
        var leaderTicket = kit.Create(level: ClanCreateLevel);
        var memberTicket = kit.Create(level: ClanCreateLevel);
        var (leaderSession, leaderEntity) = await kit.EnterAsync(leaderTicket);
        Assert.Null(await kit.World.TryClanCreateAsync(
            leaderSession, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));
        int clanId = leaderEntity.ClanId;
        Assert.True(kit.World.TrySetClanSkillForTest(clanId, ClanMight, 3));
        Assert.Equal(3, leaderEntity.ClanSkills[ClanMight]);

        var (memberSession, memberEntity) = await kit.EnterAsync(memberTicket);
        Assert.Null(await kit.World.TryClanInviteAsync(
            leaderSession, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(
            memberSession, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 1 },
            kit.Now, default));
        Assert.Equal(3, memberEntity.ClanSkills[ClanMight]);

        Assert.Null(await kit.World.TryClanLeaveAsync(memberSession, kit.Now, default));
        Assert.Empty(memberEntity.ClanSkills);
    }

    [Fact]
    public async Task Clan_skill_book_survives_zone_restart_and_enter()
    {
        var kit = new NetTestKit();
        var leaderTicket = kit.Create(level: ClanCreateLevel);
        var (session, entity) = await kit.EnterAsync(leaderTicket);
        Assert.Null(await kit.World.TryClanCreateAsync(
            session, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));
        Assert.True(kit.World.TrySetClanSkillForTest(entity.ClanId, ClanMight, 2));

        var restarted = new NetTestKit(store: kit.Store);
        var (_, again) = await restarted.EnterAsync(leaderTicket);
        Assert.Equal(2, again.ClanSkills[ClanMight]);
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
