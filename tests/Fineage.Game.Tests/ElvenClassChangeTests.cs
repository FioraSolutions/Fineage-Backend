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
    static string Page(QuestOutcome outcome) => PageName(outcome.Html);

    static string PageName(string html)
    {
        var dir = Path.Combine(FindRoot(), "ref/mobius-ct0-interlude/data/scripts/quests/Q00408_PathOfTheElvenWizard");
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, "*.htm*"))
            {
                string body = File.ReadAllText(file).Trim();
                string first = body.Split("<a ")[0];
                if (html.StartsWith(first, StringComparison.Ordinal))
                    return Path.GetFileName(file);
            }
        }

        return html;
    }

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Fineage.Backend.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? ".";
    }
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
            Assert.True(Data.Items.TryGet(change.RewardItemId, out _));
            foreach (var option in change.Options)
                Assert.True(Data.Items.TryGet(option.ProofItemId, out _));
        }
    }
}
