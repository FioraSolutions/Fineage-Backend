using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class TransferSkillCertificateTests
{
    [Fact]
    public async Task Pomander_consumes_ledger_and_grants_transfer_skill_to_known_skills()
    {
        const int BishopClass = 16;
        const int Pomander = 15307;
        const int Recharge = 1013;
        const int RechargeLevel = 32;

        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        player.ClassId = BishopClass;
        player.Level = 76;
        player.KnownSkills.Clear();
        player.LearnedSkills.Clear();

        Assert.Null(await kit.World.TryGiveItemAsync(session, Pomander, 1, default));
        var pomander = player.Inventory!.Items.First(i => i.ItemId == Pomander);

        Assert.Null(await kit.World.TryUseItemAsync(session, pomander.ObjectId, default));

        Assert.True(player.KnownSkills.TryGetValue(Recharge, out int lvl));
        Assert.Equal(RechargeLevel, lvl);
        Assert.Equal(0, player.Inventory.CountOf(Pomander));

        var learn = Assert.Single(
            kit.Ledger.Entries,
            e => e.Transaction.Kind == LedgerTxKind.TrainerSkillLearn && e.Transaction.Reference == Recharge);
        Assert.NotNull(learn.Transaction);
    }

    [Fact]
    public async Task Acquire_transfer_skill_consumes_matching_pomander()
    {
        const int CardinalClass = 97;
        const int Pomander = 15307;
        const int Recharge = 1013;
        const int RechargeLevel = 32;

        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        player.ClassId = CardinalClass;
        player.Level = 76;
        player.KnownSkills.Clear();
        player.LearnedSkills.Clear();

        Assert.Null(await kit.World.TryGiveItemAsync(session, Pomander, 1, default));
        Assert.Null(await kit.World.TryAcquireSkillAsync(
            session, Recharge, RechargeLevel, ZoneWorld.AcquireSkillTypeTransfer, informing: false, default));

        Assert.Equal(RechargeLevel, player.KnownSkills[Recharge]);
        Assert.Equal(0, player.Inventory!.CountOf(Pomander));
    }

    [Fact]
    public async Task Unknown_certificate_id_logs_and_refuses_use()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();
        Assert.Null(kit.World.RefuseUnknownTransferCertificateForTest(session, 88_888));
    }
}