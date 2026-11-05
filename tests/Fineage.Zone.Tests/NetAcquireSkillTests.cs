using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius trainer window over Net: <c>Folk.showSkillList</c> answers with <c>AcquireSkillList</c>,
/// <c>RequestAcquireSkillInfo</c> with <c>AcquireSkillInfo</c>, and <c>RequestAcquireSkill</c> buys the row
/// (SP taken, skill learned, refreshed SkillList + a new AcquireSkillList without the bought row).
/// </summary>
public sealed class NetAcquireSkillTests
{
    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Elf, NetBridgeRecorder Rec)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync();
        // Mobius trainer mode: only autoGet rows are free, NPC rows must be bought with SP.
        kit.World.TrainerSpLearn = true;
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, elf, rec);
    }

    static AcquireSkillListNetMsg LastList(NetBridgeRecorder rec) =>
        Assert.IsType<AcquireSkillListNetMsg>(rec.Of(nameof(INetPresenceBridge.FanoutAcquireSkillList)).Last()[1]);

    [Fact]
    public async Task Trainer_opens_the_acquire_skill_list_window()
    {
        var (kit, session, elf, rec) = await SetupAsync();
        elf.Level = 20;
        elf.KnownSkills.Clear(); // a fresh level 20: the trainer owes rows
        kit.World.TrainerServiceForTest(session, NpcServiceIntent.SkillList);

        var list = LastList(rec);
        Assert.Equal(ZoneWorld.AcquireSkillTypeUsual, list.SkillType);
        Assert.NotEmpty(list.Skills);
        Assert.All(list.Skills, row =>
        {
            Assert.True(row.SkillId > 0);
            Assert.True(row.NextLevel >= 1);
            Assert.True(row.MaxLevel >= row.NextLevel);
            Assert.True(row.SpCost >= 0);
        });
    }

    [Fact]
    public async Task Acquire_skill_info_answers_the_confirmation_page()
    {
        var (kit, session, elf, rec) = await SetupAsync();
        elf.Level = 20;
        elf.KnownSkills.Clear();
        kit.World.TrainerServiceForTest(session, NpcServiceIntent.SkillList);
        var offer = LastList(rec).Skills[0];

        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, offer.SkillId, offer.NextLevel, ZoneWorld.AcquireSkillTypeUsual, informing: true, default));

        var info = Assert.IsType<AcquireSkillInfoNetMsg>(
            Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutAcquireSkillInfo)))[1]);
        Assert.Equal(offer.SkillId, info.SkillId);
        Assert.Equal(offer.NextLevel, info.Level);
        Assert.Equal(offer.SpCost, info.SpCost);
        Assert.Equal(ZoneWorld.AcquireSkillTypeUsual, info.SkillType);
        Assert.Empty(info.Requirements);
    }

    [Fact]
    public async Task Acquire_skill_spends_the_sp_and_drops_the_row_from_the_list()
    {
        var (kit, session, elf, rec) = await SetupAsync();
        elf.Level = 20;
        elf.KnownSkills.Clear();
        kit.World.TrainerServiceForTest(session, NpcServiceIntent.SkillList);
        var offer = LastList(rec).Skills[0];
        elf.Sp = offer.SpCost + 100;

        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, offer.SkillId, offer.NextLevel, ZoneWorld.AcquireSkillTypeUsual, informing: false, default));

        Assert.Equal(100, elf.Sp);
        Assert.True(elf.KnownSkills.ContainsKey(offer.SkillId));
        Assert.Equal(offer.NextLevel, elf.KnownSkills[offer.SkillId]);
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutSkillList)));

        // Mobius re-sends the list after a learn: the bought level is gone, and the skill's next level
        // (if the tree has one at this character level) takes its place.
        var refreshed = LastList(rec);
        Assert.DoesNotContain(refreshed.Skills, row => row.SkillId == offer.SkillId && row.NextLevel == offer.NextLevel);
        Assert.All(refreshed.Skills, row => Assert.True(row.NextLevel > elf.KnownSkills.GetValueOrDefault(row.SkillId)));
    }

    [Fact]
    public async Task Acquire_skill_without_enough_sp_keeps_the_row_and_sends_message_278()
    {
        var (kit, session, elf, rec) = await SetupAsync();
        elf.Level = 20;
        elf.KnownSkills.Clear();
        kit.World.TrainerServiceForTest(session, NpcServiceIntent.SkillList);
        var offer = LastList(rec).Skills[0];
        elf.Sp = 0;

        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, offer.SkillId, offer.NextLevel, ZoneWorld.AcquireSkillTypeUsual, informing: false, default));

        Assert.Equal(0, elf.Sp);
        Assert.False(elf.KnownSkills.ContainsKey(offer.SkillId));
        var message = Assert.IsType<SystemMessageNetMsg>(
            Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)))[1]);
        Assert.Equal(278, message.Id);
        Assert.Contains(LastList(rec).Skills, row => row.SkillId == offer.SkillId);
    }

    [Fact]
    public async Task Acquire_skill_rejects_a_row_that_is_not_on_offer()
    {
        var (kit, session, elf, rec) = await SetupAsync();
        elf.Level = 20;
        elf.KnownSkills.Clear();

        Assert.NotNull(await kit.World.TryAcquireSkillAsync(
            session, 999_999, 1, ZoneWorld.AcquireSkillTypeUsual, informing: false, default));
    }

    /// <summary>
    /// The trainer NPC used to be one field on the world. Two players in the same zone share that world,
    /// so player B opening a trainer window silently re-pointed player A's purchase at B's trainer —
    /// A's learn request then echoed the wrong NPC's page back to A.
    /// </summary>
    [Fact]
    public async Task Two_players_keep_their_own_trainer()
    {
        var (kit, session, elf, rec) = await SetupAsync();
        var (otherSession, otherElf) = await kit.EnterAsync();
        elf.Level = 20;
        elf.KnownSkills.Clear();
        otherElf.Level = 20;
        otherElf.KnownSkills.Clear();

        kit.World.TrainerServiceForTest(session, NpcServiceIntent.SkillList, npcObjectId: 300_001);
        kit.World.TrainerServiceForTest(otherSession, NpcServiceIntent.SkillList, npcObjectId: 300_002);

        // Both opened a window; each session must remember its own trainer, not the last one seen.
        Assert.Equal(300_001, session.TrainerNpcId);
        Assert.Equal(300_002, otherSession.TrainerNpcId);

        // A purchase from the first player must still resolve against the first player's trainer.
        var offer = LastList(rec).Skills[0];
        elf.Sp = offer.SpCost + 1;
        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, offer.SkillId, offer.NextLevel, ZoneWorld.AcquireSkillTypeUsual, informing: false, default));
        Assert.True(elf.KnownSkills.ContainsKey(offer.SkillId));
    }
}
