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

    [Fact]
    public async Task Second_class_transfer_needs_all_three_marks_and_gives_c_grade_coupons()
    {
        int[] marks = [2627, 2734, 2762]; // Challenger, Trust, Duelist -> Gladiator
        foreach (int mark in marks.AsSpan(0, 2).ToArray())
        {
            var partial = new NetTestKit();
            var (session, player) = await partial.EnterAsync(classId: 1);
            player.Level = 40;
            foreach (int have in marks)
            {
                if (have != mark)
                    player.Inventory!.Add(have, 1);
            }

            Assert.Null(partial.World.VillageMasterForTest(session, 30109, "ElfHumanFighterChange2", "2", out var html));
            Assert.Contains("mark", html, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, player.ClassId);
            Assert.Equal(2, marks.Count(id => player.Inventory.CountOf(id) > 0));
        }

        var kit = new NetTestKit();
        var (ok, warrior) = await kit.EnterAsync(classId: 1);
        warrior.Level = 40;
        foreach (int mark in marks)
            warrior.Inventory!.Add(mark, 2); // Mobius takeItems(mark, -1): the whole stack goes

        Assert.Null(kit.World.VillageMasterForTest(ok, 30109, "ElfHumanFighterChange2", "2", out var success));
        Assert.Contains("Congratulations", success);
        Assert.Equal(2, warrior.ClassId);
        Assert.Equal(2, ok.ClassId);
        foreach (int mark in marks)
            Assert.Equal(0, warrior.Inventory.CountOf(mark));
        Assert.Equal(15, warrior.Inventory.CountOf(8870));
        Assert.Equal(2, kit.Store.FindById(ok.Account, ok.ObjectId)!.ClassId);
        Assert.Equal(kit.Game.Classes!.Get(2).ModifiedHp(40), warrior.MaxHp);
    }

    [Fact]
    public async Task Second_class_transfer_refuses_level_39()
    {
        var kit = new NetTestKit();
        var (session, warrior) = await kit.EnterAsync(classId: 1);
        warrior.Level = 39;
        foreach (int mark in new[] { 2627, 2734, 2762 })
            warrior.Inventory!.Add(mark, 1);

        Assert.Null(kit.World.VillageMasterForTest(session, 30109, "ElfHumanFighterChange2", "2", out var html));
        Assert.Contains("must be over 40", html);
        Assert.Equal(1, warrior.ClassId);
        Assert.Equal(1, warrior.Inventory.CountOf(2627));
    }
}
