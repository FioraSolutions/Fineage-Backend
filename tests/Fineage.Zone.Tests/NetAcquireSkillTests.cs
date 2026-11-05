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

    /// <summary>
    /// Mobius gates Divine Inspiration (1405) behind the Ancient Book items (8618/8619/8620/8621), one per level.
    /// The confirmation page must list the book, and a purchase without it must fail with SysMsg 276.
    /// </summary>
    [Fact]
    public async Task Book_skill_needs_the_item_and_lists_it_on_the_confirmation_page()
    {
        var (kit, session, elf, rec) = await SetupAsync();
        elf.Level = 52;
        elf.KnownSkills.Clear();

        kit.World.TrainerServiceForTest(session, NpcServiceIntent.SkillList);
        var offer = Assert.Single(LastList(rec).Skills, row => row.SkillId == 1405 && row.NextLevel == 1);
        Assert.Equal(2, offer.Requirements); // Mobius marks a book row as "has a requirement"

        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, 1405, 1, ZoneWorld.AcquireSkillTypeUsual, informing: true, default));
        var info = Assert.IsType<AcquireSkillInfoNetMsg>(
            Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutAcquireSkillInfo)))[1]);
        var req = Assert.Single(info.Requirements);
        Assert.Equal(99, req.Type);
        Assert.Equal(8618, req.ItemId);
        Assert.Equal(1, req.Count);

        // Without the book the purchase is refused and the row stays on the list.
        elf.Sp = 10_000_000;
        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, 1405, 1, ZoneWorld.AcquireSkillTypeUsual, informing: false, default));
        Assert.False(elf.KnownSkills.ContainsKey(1405));
        Assert.Equal(276, Assert.IsType<SystemMessageNetMsg>(
            Assert.Single(rec.Of(nameof(INetPresenceBridge.FanoutSystemMessage)))[1]).Id);
        Assert.Contains(LastList(rec).Skills, row => row.SkillId == 1405 && row.NextLevel == 1);

        // With the book the skill is learned and the book leaves the bag.
        elf.Inventory!.Add(8618, 1);
        var spBefore = elf.Sp;
        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, 1405, 1, ZoneWorld.AcquireSkillTypeUsual, informing: false, default));
        Assert.Equal(1, elf.KnownSkills[1405]);
        Assert.Equal(0, elf.Inventory.CountOf(8618));
        Assert.Equal(spBefore - offer.SpCost, elf.Sp);
        Assert.NotEmpty(rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)));
    }
}
