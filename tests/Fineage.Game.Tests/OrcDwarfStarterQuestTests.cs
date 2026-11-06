using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q107 / Q108 from scripts/gen_race_starters_107_108.py.</summary>
public sealed class OrcDwarfStarterQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Orc(int level) => new(3, level, 44);
    static QuestActor Dwarf(int level) => new(4, level, 53);

    [Fact]
    public void Q107_merciless_punishment_three_letters()
    {
        var (m, bag, j) = Setup();
        const int Q = 107, Hatos = 30568, Parugon = 30580, Mob = 27041;
        const int O2 = 1554, O3 = 1555, LHu = 1557, LElf = 1558, Butcher = 1510;
        var me = Orc(10);

        Assert.True(m.AdvEvent(j, bag, Q, Hatos, "30568-04", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Parugon, me).Ok);
        Assert.Equal(2, j.Find(Q)!.Cond);

        m.OnKill(j, bag, Mob, me);
        Assert.Equal(3, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(LHu));

        Assert.True(m.AdvEvent(j, bag, Q, Hatos, "30568-08", me).Ok);
        Assert.Equal(4, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(O2));

        m.OnKill(j, bag, Mob, me);
        Assert.Equal(5, j.Find(Q)!.Cond);
        Assert.True(m.AdvEvent(j, bag, Q, Hatos, "30568-10", me).Ok);
        Assert.Equal(6, j.Find(Q)!.Cond);

        m.OnKill(j, bag, Mob, me);
        Assert.Equal(7, j.Find(Q)!.Cond);
        Assert.Equal(1, bag.CountOf(LElf));

        var done = m.Talk(j, bag, Q, Hatos, me);
        Assert.True(done.Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
        Assert.Equal(1, bag.CountOf(Butcher));
        Assert.Equal(34565, done.Exp);
        Assert.Equal(14666, bag.CountOf(57));
        Assert.Equal(0, bag.CountOf(O3));
    }

    [Fact]
    public void Q108_diamond_fuss_delivery_and_gems()
    {
        var (m, bag, j) = Setup(0.0); // gems always drop
        const int Q = 108;
        const int Gouph = 30523, Reep = 30516, Torocco = 30555, Maron = 30529;
        const int Brunon = 30526, Murdoc = 30521, Airy = 30522;
        const int Aqua = 1564, Chrys = 1565, Star = 1571, Hammer = 1511;
        var me = Dwarf(10);

        Assert.True(m.AdvEvent(j, bag, Q, Gouph, "30523-04", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Reep, me).Ok);
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Torocco, "30555-02", me).Ok);
        Assert.Equal(3, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Maron, me).Ok);
        Assert.Equal(4, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Brunon, "30526-02", me).Ok);
        Assert.Equal(5, j.Find(Q)!.Cond);

        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20323, me);
        Assert.Equal(10, bag.CountOf(Aqua));
        Assert.Equal(10, bag.CountOf(Chrys));

        Assert.True(m.Talk(j, bag, Q, Brunon, me).Ok); // gem box
        Assert.Equal(7, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Gouph, me).Ok); // coal
        Assert.Equal(8, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Brunon, me).Ok); // letter
        Assert.Equal(9, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Murdoc, me).Ok);
        Assert.Equal(10, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Airy, me).Ok);
        Assert.Equal(11, j.Find(Q)!.Cond);

        m.OnKill(j, bag, 20480, me); // star @20% with roll 0
        Assert.Equal(1, bag.CountOf(Star));
        Assert.Equal(12, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Gouph, me);
        Assert.True(done.Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
        Assert.Equal(1, bag.CountOf(Hammer));
        Assert.Equal(34565, done.Exp);
    }
}
