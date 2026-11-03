using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Village-master class transfer applied in the zone: proof taken, class set and saved, coupons given, skills refreshed.</summary>
public sealed class ClassChangeTests
{
    [Fact]
    public async Task Elven_mystic_with_eternity_diamond_becomes_elven_wizard()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync(classId: 25);
        elf.Level = 20;
        elf.Inventory!.Add(1230, 1);

        Assert.Null(kit.World.VillageMasterForTest(session, 30037, "ElfHumanWizardChange1", "26", out var html));
        Assert.Contains("Congratulations", html);
        Assert.Equal(26, elf.ClassId);
        Assert.Equal(26, session.ClassId);
        Assert.Equal(0, elf.Inventory.CountOf(1230));
        Assert.Equal(15, elf.Inventory.CountOf(8869));
        Assert.Equal(26, kit.Store.FindById(session.Account, session.ObjectId)!.ClassId);
        Assert.Equal(kit.Game.Classes!.Get(26).ModifiedHp(20), elf.MaxHp);
    }

    [Fact]
    public async Task Low_level_or_missing_proof_keeps_the_class()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync(classId: 25);
        elf.Level = 19;
        elf.Inventory!.Add(1230, 1);
        Assert.Null(kit.World.VillageMasterForTest(session, 30037, "ElfHumanWizardChange1", "26", out var html));
        Assert.Contains("lower than 20", html);
        Assert.Equal(25, elf.ClassId);
        Assert.Equal(1, elf.Inventory.CountOf(1230));

        elf.Level = 20;
        elf.Inventory.TryTake(1230, 1, new List<Fineage.Game.Quests.BagTouch>());
        kit.World.VillageMasterForTest(session, 30037, "ElfHumanWizardChange1", "26", out _);
        Assert.Equal(25, elf.ClassId);
    }

    [Fact]
    public async Task Asterios_transfer_talk_serves_mobius_pages()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync(classId: 25);
        Assert.Null(kit.World.VillageMasterForTest(session, 30154, "FirstClassTransferTalk", "", out var html));
        Assert.Contains("Elven Wizard", html);
        Assert.DoesNotContain("not implemented", html);
    }

    [Theory]
    [InlineData(18, 1204, "ElfHumanFighterChange1", 30066, "19", 19)] // Q406 Elven Knight Brooch
    [InlineData(18, 1217, "ElfHumanFighterChange1", 30288, "22", 22)] // Q407 Reisa's Recommendation
    [InlineData(25, 1235, "ElfHumanWizardChange1", 30289, "29", 29)]  // Q409 Leaf of Oracle
    public async Task Path_quest_proofs_open_the_matching_first_class(int from, int proof, string script, int npc, string evt, int to)
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync(classId: from);
        elf.Level = 20;
        elf.Inventory!.Add(proof, 1);
        Assert.Null(kit.World.VillageMasterForTest(session, npc, script, evt, out _));
        Assert.Equal(to, elf.ClassId);
        Assert.Equal(0, elf.Inventory.CountOf(proof));
        Assert.Equal(to, kit.Store.FindById(session.Account, session.ObjectId)!.ClassId);
    }
}
