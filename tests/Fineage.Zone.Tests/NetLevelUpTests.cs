using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;
using Fineage.Protocol;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>PlayerStat.addLevel</c>: crossing an exp row runs <c>rewardSkills()</c>, so the freshly unlocked skill
/// reaches the client in a new SkillList, and the level-up SocialAction / SystemMessage 96 / StatusUpdate follow.
/// </summary>
public sealed class NetLevelUpTests
{
    [Fact]
    public async Task Level_up_refreshes_the_skill_book_on_net()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (_, elf) = await kit.EnterAsync();

        int before = elf.KnownSkills.Count;
        int skillsBefore = rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)).Count;

        // Mobius ExperienceData row for level 20 on an Elven Mystic; the exact exp is irrelevant — the level is what unlocks rows.
        elf.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(20);
        elf.Level = 19;
        kit.World.GrantExpForTest(elf, 1, 0);

        Assert.Equal(20, elf.Level);
        Assert.True(elf.KnownSkills.Count > before, "rewardSkills should add the level-20 rows to the known book");
        Assert.True(rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)).Count > skillsBefore);

        var notice = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)));
        var messages = Assert.IsType<SystemMessageMsg>(notice[1]);
        Assert.Equal(ZoneWorld.LevelUpMessageId, messages.Id);

        var stats = Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutPlayerStats)));
        Assert.Equal(20, Assert.IsType<Fineage.Protocol.PlayerStatsMsg>(stats[1]).Level);
    }

    [Fact]
    public async Task Granting_exp_below_the_row_does_not_level_up()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (_, elf) = await kit.EnterAsync();

        // One exp short of the level-20 row: same level, no rewardSkills, no level-up chatter.
        elf.Level = 19;
        elf.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(20) - 1;

        kit.World.GrantExpForTest(elf, 0, 0);

        Assert.Equal(19, elf.Level);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)));
    }
}
