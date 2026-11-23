using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class CrystallizeTests
{
    [Fact]
    public void Request_round_trips_object_id_and_count()
    {
        var back = NetCodec.DecodeCrystallizeItemRequest(NetCodec.EncodeCrystallizeItemRequest(
            new CrystallizeItemRequestMsg { ObjectId = 42, Count = 3 }));
        Assert.Equal(42, back.ObjectId);
        Assert.Equal(3, back.Count);
        Assert.Equal(0x03E3, NetOpcode.RequestCrystallizeItem);
    }

    [Fact]
    public void Skill_gate_follows_the_grade_level()
    {
        Assert.False(CrystallizeRules.SkillAllows(CrystalGrade.None, 5));
        Assert.True(CrystallizeRules.SkillAllows(CrystalGrade.D, 1));
        Assert.False(CrystallizeRules.SkillAllows(CrystalGrade.C, 1));
        Assert.True(CrystallizeRules.SkillAllows(CrystalGrade.S, 5));
    }

    [Fact]
    public async Task Crystallize_destroys_the_item_and_grants_crystals_on_the_ledger()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        var template = kit.Game.Items.Templates.First(t =>
            t.Crystal == CrystalGrade.D && t.CrystalCount > 0 && !t.Stackable && EnchantRules.IsCrystallizable(t));
        var piece = me.Inventory.Add(template.Id, 1);
        me.Player.LearnedSkills[CrystallizeRules.SkillId] = 1;
        int before = kit.Ledger.Entries.Count;

        Assert.Null(await kit.World.TryCrystallizeItemAsync(me.Session, piece.ObjectId, 1, CancellationToken.None));
        Assert.Null(me.Inventory.Find(piece.ObjectId));
        int crystalId = EnchantRules.CrystalItemId(CrystalGrade.D);
        int yield = CrystallizeRules.Yield(template, 0, 1);
        Assert.Equal(yield, me.Inventory.FindByItemId(crystalId)!.Count);
        Assert.Contains(kit.Ledger.Entries.Skip(before),
            e => e.Transaction.Kind == LedgerTxKind.Crystallize && e.Transaction.Reference == template.Id);
        Assert.Contains(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            row => Assert.IsType<SystemMessageMsg>(row[1]).Id == CrystallizeProtocol.Crystallized);
    }

    [Fact]
    public async Task Missing_skill_does_not_destroy_the_item()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        var template = kit.Game.Items.Templates.First(t => t.Crystal == CrystalGrade.D && EnchantRules.IsCrystallizable(t));
        var piece = me.Inventory.Add(template.Id, 1);

        Assert.Equal(CrystallizeProtocol.SkillTooLow, await kit.World.TryCrystallizeItemAsync(me.Session, piece.ObjectId, 1, CancellationToken.None));
        Assert.NotNull(me.Inventory.Find(piece.ObjectId));
        Assert.DoesNotContain(kit.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.Crystallize);
    }

    [Fact]
    public async Task Non_crystallizable_item_is_logged_and_kept()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        var template = kit.Game.Items.Templates.First(t => t.Crystal == CrystalGrade.None && !EnchantRules.IsCrystallizable(t));
        var piece = me.Inventory.Add(template.Id, 1);
        int count = piece.Count;

        Assert.Equal(CrystallizeProtocol.NotCrystallizable, await kit.World.TryCrystallizeItemAsync(me.Session, piece.ObjectId, 1, CancellationToken.None));
        Assert.Equal(count, me.Inventory.Find(piece.ObjectId)!.Count);
        Assert.DoesNotContain(kit.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.Crystallize);
    }

    [Fact]
    public async Task Grade_above_the_skill_is_refused()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        var template = kit.Game.Items.Templates.First(t => t.Crystal == CrystalGrade.C && EnchantRules.IsCrystallizable(t));
        var piece = me.Inventory.Add(template.Id, 1);
        me.Player.LearnedSkills[CrystallizeRules.SkillId] = 1;

        Assert.Equal(CrystallizeProtocol.SkillTooLow, await kit.World.TryCrystallizeItemAsync(me.Session, piece.ObjectId, 1, CancellationToken.None));
        Assert.NotNull(me.Inventory.Find(piece.ObjectId));
    }
}
