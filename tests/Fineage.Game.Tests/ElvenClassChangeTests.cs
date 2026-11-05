using Fineage.Game.Quests;
using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q00408 Path of the Elven Wizard on the shipped datapack (drops forced with roll 0).</summary>
public sealed class PathOfTheElvenWizardTests
{
    const int Q = 408, Rossela = 30414, Greenis = 30157, Thalia = 30371, Northwind = 30423;
    static QuestActor Mystic(int level) => new(1, level, 25);

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => 0.0), new PlayerInventory(data.Items), new QuestJournal());
    }

    [Fact]
    public void Accept_gates_class_then_level_then_diamond_like_mobius()
    {
        if (!RefAvailable)
            return; // ref/ is gitignored; page-name gates are re-checked on machines that have the sources
        var (m, bag, j) = Setup();
        Assert.Contains("30414-03", Page(m.AdvEvent(j, bag, Q, Rossela, "ACCEPT", new QuestActor(0, 20, 10))));
        Assert.Contains("30414-02a", Page(m.AdvEvent(j, bag, Q, Rossela, "ACCEPT", new QuestActor(1, 20, 26))));
        Assert.Contains("30414-04", Page(m.AdvEvent(j, bag, Q, Rossela, "ACCEPT", Mystic(17))));
        Assert.Null(j.Find(Q));
        bag.Add(1230, 1);
        Assert.Contains("30414-05", Page(m.AdvEvent(j, bag, Q, Rossela, "ACCEPT", Mystic(18))));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Path_runs_end_to_end_and_hands_out_the_eternity_diamond()
    {
        var (m, bag, j) = Setup();
        var me = Mystic(18);
        Assert.True(m.AdvEvent(j, bag, Q, Rossela, "ACCEPT", me).Ok);
        Assert.Equal(QuestPhase.Started, j.Find(Q)!.Phase);
        Assert.Equal(1, bag.CountOf(1229));

        // Ruby: letter -> Greenis charm -> 5 Red Down from Pincer Spiders -> ruby.
        Assert.True(m.AdvEvent(j, bag, Q, Rossela, "30414-10", me).Ok);
        Assert.Equal(1, bag.CountOf(1218));
        Assert.True(m.AdvEvent(j, bag, Q, Rossela, "30414-10", me).Ok);
        Assert.Equal(1, bag.CountOf(1218)); // given once
        Assert.True(m.AdvEvent(j, bag, Q, Greenis, "30157-02", me).Ok);
        Assert.Equal(0, bag.CountOf(1218));
        Assert.Equal(1, bag.CountOf(1272));
        for (int i = 0; i < 7; i++)
            m.OnKill(j, bag, 20466, me);
        Assert.Equal(5, bag.CountOf(1219));
        Assert.True(m.Talk(j, bag, Q, Greenis, me).Ok);
        Assert.Equal(1, bag.CountOf(1220));
        Assert.Equal(0, bag.CountOf(1272));

        // Aquamarine: apple -> Thalia sap -> 5 Gold Leaves from Dryad Elders -> aquamarine.
        Assert.True(m.AdvEvent(j, bag, Q, Rossela, "30414-12", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Thalia, "30371-02", me).Ok);
        Assert.Equal(1, bag.CountOf(1273));
        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20019, me);
        Assert.True(m.Talk(j, bag, Q, Thalia, me).Ok);
        Assert.Equal(1, bag.CountOf(1221));

        // Amethyst: love -> Northwind potpourri -> 2 Amethyst from Sukar Wererat Leaders -> nobility amethyst.
        Assert.True(m.AdvEvent(j, bag, Q, Rossela, "30414-16", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Northwind, me).Ok);
        Assert.Equal(1, bag.CountOf(1274));
        if (RefAvailable)
            Assert.Contains("30423-02", Page(m.Talk(j, bag, Q, Northwind, me)));
        m.OnKill(j, bag, 20047, me);
        m.OnKill(j, bag, 20047, me);
        Assert.True(m.Talk(j, bag, Q, Northwind, me).Ok);
        Assert.Equal(1, bag.CountOf(1226));

        var done = m.Talk(j, bag, Q, Rossela, me);
        Assert.True(done.Ok);
        Assert.Equal(591724, done.Exp);
        Assert.Equal(35928, done.Sp);
        Assert.Equal(163800, bag.CountOf(57));
        Assert.Equal(1, bag.CountOf(1230));
        Assert.Equal(0, bag.CountOf(1229));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Kill_drops_need_the_carrier_item()
    {
        var (m, bag, j) = Setup();
        Assert.True(m.AdvEvent(j, bag, Q, Rossela, "ACCEPT", Mystic(18)).Ok);
        m.OnKill(j, bag, 20466, Mystic(18));
        Assert.Equal(0, bag.CountOf(1219));
    }

    // Html is the Mobius page text; identify it by a phrase unique to that page file.
    // Without ref/ (gitignored, absent in CI) the raw html is returned and the page-name
    // assertions above cannot run, so the whole test returns early instead.
    static string? Page(QuestOutcome outcome) => PageName(outcome.Html);

    static string? PageName(string html)
    {
        string? dir = MobiusRef.QuestDir("Q00408_PathOfTheElvenWizard");
        if (dir == null)
            return null;

        foreach (var file in Directory.GetFiles(dir, "*.htm*"))
        {
            string body = File.ReadAllText(file).Trim();
            string first = body.Split("<a ")[0];
            if (html.StartsWith(first, StringComparison.Ordinal))
                return Path.GetFileName(file);
        }

        return html;
    }

    /// <summary>Mobius page files are only present when the gitignored <c>ref/</c> tree is checked out.</summary>
    static bool RefAvailable => MobiusRef.QuestDir("Q00408_PathOfTheElvenWizard") != null;
}

/// <summary>Mobius village_master FirstClassTransferTalk + ElfHumanWizardChange1 / ElfHumanFighterChange1 decisions.</summary>
public sealed class VillageMasterTests
{
    static readonly GameData Data = GameData.LoadDefault();
    static readonly VillageMasterCatalog Masters = VillageMasterCatalog.LoadDefault();
    static ClassActor Who(int race, int classId, int level) => Masters.Actor(Data.Classes, race, classId, level);

    [Fact]
    public void Asterios_only_talks_about_the_transfer()
    {
        Assert.Equal("30154_mystic.html", Masters.Talk("FirstClassTransferTalk", 30154, Who(1, 25, 10))!.Page);
        Assert.Equal("30154_fighter.html", Masters.Talk("FirstClassTransferTalk", 30154, Who(1, 18, 10))!.Page);
        Assert.Equal("30154_no.html", Masters.Talk("FirstClassTransferTalk", 30154, Who(0, 10, 10))!.Page);
        Assert.Equal("30154_transfer_1.html", Masters.Talk("FirstClassTransferTalk", 30154, Who(1, 26, 25))!.Page);
        Assert.Equal("30154_elven_wizard_01.html",
            Masters.Event("FirstClassTransferTalk", 30154, "30154_elven_wizard_01.html", Who(1, 25, 10), _ => 0)!.Page);
        Assert.Null(Masters.Event("FirstClassTransferTalk", 30154, "../30026_no.html", Who(1, 25, 10), _ => 0));
        Assert.Null(Masters.Event("FirstClassTransferTalk", 30154, "30026_fighter.html", Who(1, 25, 10), _ => 0));
    }

    [Fact]
    public void Wizard_master_lists_by_race_and_group()
    {
        Assert.Equal("30037-08.htm", Masters.Talk("ElfHumanWizardChange1", 30037, Who(1, 25, 20))!.Page);
        Assert.Equal("30037-01.htm", Masters.Talk("ElfHumanWizardChange1", 30037, Who(0, 10, 20))!.Page);
        Assert.Equal("30037-15.htm", Masters.Talk("ElfHumanWizardChange1", 30037, Who(1, 18, 20))!.Page);
        Assert.Equal("30037-15.htm", Masters.Talk("ElfHumanWizardChange1", 30037, Who(2, 38, 20))!.Page);
        Assert.Equal("30037-09.htm", Masters.Event("ElfHumanWizardChange1", 30037, "30037-09.htm", Who(1, 25, 20), _ => 0)!.Page);
        Assert.Null(Masters.Event("ElfHumanWizardChange1", 30037, "30037-28.htm", Who(1, 25, 20), _ => 0));
    }

    [Fact]
    public void Elven_wizard_transfer_gates_level_and_proof()
    {
        Func<int, int> diamond = id => id == 1230 ? 1 : 0;
        Func<int, int> none = _ => 0;
        Assert.Equal("30037-26.htm", Masters.Event("ElfHumanWizardChange1", 30037, "26", Who(1, 25, 19), diamond)!.Page);
        Assert.Equal("30037-27.htm", Masters.Event("ElfHumanWizardChange1", 30037, "26", Who(1, 25, 19), none)!.Page);
        Assert.Equal("30037-29.htm", Masters.Event("ElfHumanWizardChange1", 30037, "26", Who(1, 25, 20), none)!.Page);
        Assert.Null(Masters.Event("ElfHumanWizardChange1", 30037, "26", Who(1, 25, 20), none)!.Change);
        Assert.Equal("30037-16.htm", Masters.Event("ElfHumanWizardChange1", 30037, "26", Who(1, 26, 30), diamond)!.Page);
        Assert.Null(Masters.Event("ElfHumanWizardChange1", 30037, "11", Who(1, 25, 20), diamond)); // human class for an elf
        var ok = Masters.Event("ElfHumanWizardChange1", 30037, "26", Who(1, 25, 20), diamond)!;
        Assert.Equal("30037-28.htm", ok.Page);
        Assert.Equal(26, ok.Change!.ClassId);
        Assert.Equal(8869, ok.RewardItemId);
        Assert.Equal(15, ok.RewardCount);
    }

    [Fact]
    public void Fighter_master_covers_elven_knight_and_scout()
    {
        Func<int, int> brooch = id => id == 1204 ? 1 : 0;
        Assert.Equal("30066-11.htm", Masters.Talk("ElfHumanFighterChange1", 30066, Who(1, 18, 20))!.Page);
        Assert.Equal(19, Masters.Event("ElfHumanFighterChange1", 30066, "19", Who(1, 18, 20), brooch)!.Change!.ClassId);
        Assert.Equal("30066-40.htm", Masters.Event("ElfHumanFighterChange1", 30066, "22", Who(1, 18, 20), brooch)!.Page);
    }

    [Fact]
    public void Proof_and_reward_items_exist_and_proofs_are_quest_items()
    {
        foreach (var change in Masters.Changes)
        {
            if (change.RewardItemId > 0)
                Assert.True(Data.Items.TryGet(change.RewardItemId, out _));
            foreach (var option in change.Options)
                foreach (int proof in option.ProofItemIds)
                    Assert.True(Data.Items.TryGet(proof, out _));
        }
    }

    [Fact]
    public void Second_class_master_needs_all_marks_and_pays_c_grade()
    {
        int[] marks = [2627, 2734, 2762];
        Func<int, int> all = id => marks.Contains(id) ? 1 : 0;
        Func<int, int> two = id => id == 2734 || id == 2762 ? 1 : 0;

        var warrior = Who(0, 1, 40);
        Assert.Equal("30109-02.htm", Masters.Talk("ElfHumanFighterChange2", 30109, warrior)!.Page);
        Assert.Equal("30109-43.htm", Masters.Event("ElfHumanFighterChange2", 30109, "2", warrior, two)!.Page); // no proof
        Assert.Equal("30109-40.htm", Masters.Event("ElfHumanFighterChange2", 30109, "2", Who(0, 1, 39), all)!.Page); // low level
        Assert.Equal("30109-41.htm", Masters.Event("ElfHumanFighterChange2", 30109, "2", Who(0, 1, 39), two)!.Page);

        var ok = Masters.Event("ElfHumanFighterChange2", 30109, "2", warrior, all)!;
        Assert.Equal("30109-42.htm", ok.Page);
        Assert.Equal(2, ok.Change!.ClassId);
        Assert.Equal(8870, ok.RewardItemId);
        Assert.Equal(15, ok.RewardCount);
    }

    [Fact]
    public void Second_class_master_redirects_characters_that_already_moved_on()
    {
        // Mobius guards run before the class branch: a 3rd-class fighter is told so, not transferred.
        Assert.Equal("30109-39.htm", Masters.Event("ElfHumanFighterChange2", 30109, "2", Who(0, 2, 40), _ => 1)!.Page);
        Assert.Equal("30109-01.htm", Masters.Talk("ElfHumanFighterChange2", 30109, Who(0, 88, 76))!.Page);
        Assert.Equal("30109-38.htm", Masters.Talk("ElfHumanFighterChange2", 30109, Who(1, 25, 40))!.Page); // a mystic
        Assert.Equal("30109-37.htm", Masters.Talk("ElfHumanFighterChange2", 30109, Who(0, 0, 40))!.Page); // still a base class
    }

    [Fact]
    public void Dark_elf_masters_use_their_own_table_indexes()
    {
        Assert.Equal("30462-18.html", Masters.Event("DarkElfChange1", 30462, "0", Who(2, 31, 20), id => id == 1244 ? 1 : 0)!.Page);
        Assert.Equal("30462-17.html", Masters.Event("DarkElfChange1", 30462, "0", Who(2, 31, 20), _ => 0)!.Page);
        Assert.Equal("30462-15.html", Masters.Event("DarkElfChange1", 30462, "0", Who(2, 31, 19), _ => 0)!.Page);
        Assert.Equal("30462-32.html", Masters.Talk("DarkElfChange1", 30462, Who(2, 32, 20))!.Page);
        Assert.Equal("30462-33.html", Masters.Talk("DarkElfChange1", 30462, Who(0, 1, 20))!.Page);
        Assert.Null(Masters.Event("DarkElfChange1", 30462, "9", Who(2, 31, 20), _ => 1)); // no such option
    }
}
