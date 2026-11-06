using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q101 / Q104 / Q106 race starters from scripts/gen_race_starters_101_104_106.py.</summary>
public sealed class RaceStarterQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor HumanFighter(int level) => new(0, level, 0);
    static QuestActor HumanMage(int level) => new(0, level, 10);
    static QuestActor DarkElfFighter(int level) => new(2, level, 31);
    static QuestActor DarkElfMage(int level) => new(2, level, 38);

    [Fact]
    public void Q101_sword_of_solidarity_end_to_end()
    {
        var (m, bag, j) = Setup(0.0); // 20% blade always hits
        const int Q = 101, Roien = 30008, Altran = 30283;
        const int Letter = 796, Dir = 937, Top = 741, Bottom = 740, Note = 742, Handle = 739, Sword = 738;
        var me = HumanFighter(9);

        Assert.True(m.AdvEvent(j, bag, Q, Roien, "30008-04", me).Ok);
        Assert.Equal(1, bag.CountOf(Letter));

        Assert.True(m.AdvEvent(j, bag, Q, Altran, "30283-02", me).Ok);
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(Dir));

        m.OnKill(j, bag, 20361, me); // top
        Assert.Equal(1, bag.CountOf(Top));
        m.OnKill(j, bag, 20362, me); // bottom → cond 3
        Assert.Equal(1, bag.CountOf(Bottom));
        Assert.Equal(3, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Altran, me).Ok);
        Assert.Equal(4, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(Note));

        Assert.True(m.Talk(j, bag, Q, Roien, me).Ok);
        Assert.Equal(5, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(Handle));

        var done = m.AdvEvent(j, bag, Q, Altran, "30283-07", me);
        Assert.True(done.Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
        Assert.Equal(1, bag.CountOf(Sword));
        Assert.Equal(25747, done.Exp);
        Assert.Equal(10981, bag.CountOf(57));
    }

    [Fact]
    public void Q104_spirit_of_mirrors_three_wands()
    {
        var (m, bag, j) = Setup();
        const int Q = 104, Gallint = 30017;
        const int Oak = 748, W1 = 1135, W2 = 1136, W3 = 1137, Adept = 747;
        var me = HumanMage(10);

        Assert.True(m.AdvEvent(j, bag, Q, Gallint, "30017-04", me).Ok);
        Assert.Equal(3, bag.CountOf(Oak));
        Assert.Equal(1, j.Find(Q)!.Cond);

        m.OnKill(j, bag, 27003, me);
        m.OnKill(j, bag, 27004, me);
        m.OnKill(j, bag, 27005, me);
        Assert.Equal(0, bag.CountOf(Oak));
        Assert.Equal(1, bag.CountOf(W1));
        Assert.Equal(1, bag.CountOf(W2));
        Assert.Equal(1, bag.CountOf(W3));
        Assert.Equal(3, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Gallint, me);
        Assert.True(done.Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
        Assert.Equal(1, bag.CountOf(Adept));
        Assert.Equal(39750, done.Exp);
        Assert.Equal(16866, bag.CountOf(57));
    }

    [Fact]
    public void Q106_forgotten_truth_mage_gets_staff()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 106, Thifiell = 30358, Karta = 30133, Mob = 27070;
        const int T2 = 985, Trans = 988, Staff = 2373;
        var me = DarkElfMage(10);

        Assert.True(m.AdvEvent(j, bag, Q, Thifiell, "30358-05", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Karta, me).Ok);
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(T2));

        m.OnKill(j, bag, Mob, me); // scroll
        m.OnKill(j, bag, Mob, me); // tablet → cond 3
        Assert.Equal(3, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Karta, me).Ok);
        Assert.Equal(4, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(Trans));

        var done = m.Talk(j, bag, Q, Thifiell, me);
        Assert.True(done.Ok);
        Assert.Equal(1, bag.CountOf(Staff));
        Assert.Equal(0, bag.CountOf(989));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q106_fighter_gets_dagger()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 106, Thifiell = 30358, Karta = 30133, Mob = 27070;
        var me = DarkElfFighter(10);

        Assert.True(m.AdvEvent(j, bag, Q, Thifiell, "30358-05", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Karta, me).Ok);
        m.OnKill(j, bag, Mob, me);
        m.OnKill(j, bag, Mob, me);
        Assert.True(m.Talk(j, bag, Q, Karta, me).Ok);
        Assert.True(m.Talk(j, bag, Q, Thifiell, me).Ok);
        Assert.Equal(1, bag.CountOf(989));
        Assert.Equal(0, bag.CountOf(2373));
    }
}
