using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q632 / Q633 / Q601 / Q602 from scripts/gen_quests_63_71_necro_argos.py.</summary>
public sealed class NecroArgos63to71QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q632_hearts_turn_in_loop()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 632, Wiz = 31522, Heart = 7542;
        var me = Any(63);
        Assert.True(m.AdvEvent(j, bag, Q, Wiz, "31522-104", me).Ok);

        for (int i = 0; i < 200; i++)
            m.OnPartyKill(j, bag, Q, 21593, me); // 59.2%
        Assert.Equal(200, bag.CountOf(Heart));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Wiz, "31522-202", me).Ok);
        Assert.Equal(0, bag.CountOf(Heart));
        Assert.Equal(120000, bag.CountOf(57));
        Assert.Equal(1, j.Find(Q)!.Cond);
    }

    [Fact]
    public void Q633_ribs_turn_in()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 633, Mina = 31388, Rib = 7544;
        var me = Any(65);
        Assert.True(m.AdvEvent(j, bag, Q, Mina, "31388-03", me).Ok);

        for (int i = 0; i < 200; i++)
            m.OnPartyKill(j, bag, Q, 21601, me); // 67.7%
        Assert.Equal(200, bag.CountOf(Rib));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.AdvEvent(j, bag, Q, Mina, "31388-07", me);
        Assert.True(done.Ok);
        Assert.Equal(0, bag.CountOf(Rib));
        Assert.Equal(25000, bag.CountOf(57));
        Assert.Equal(305235, done.Exp);
        Assert.Equal(1, j.Find(Q)!.Cond);
    }

    [Fact]
    public void Q601_watching_eyes_adena()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 601, Eye = 31683, Proof = 7188;
        var me = Any(71);
        Assert.True(m.AdvEvent(j, bag, Q, Eye, "31683-02", me).Ok);

        for (int i = 0; i < 100; i++)
            m.OnKill(j, bag, 21306, me); // 85%
        Assert.Equal(100, bag.CountOf(Proof));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Eye, "31683-05", me).Ok);
        Assert.Null(j.Find(Q));
        Assert.Equal(230000, bag.CountOf(57));
    }

    [Fact]
    public void Q602_shadow_of_light()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 602, Eye = 31683, Dark = 7189;
        var me = Any(68);
        Assert.True(m.AdvEvent(j, bag, Q, Eye, "31683-02", me).Ok);

        for (int i = 0; i < 100; i++)
            m.OnKill(j, bag, 21304, me); // 80%
        Assert.Equal(100, bag.CountOf(Dark));

        var done = m.AdvEvent(j, bag, Q, Eye, "31683-05", me);
        Assert.True(done.Ok);
        Assert.Null(j.Find(Q));
        Assert.Equal(100000, bag.CountOf(57));
        Assert.Equal(140000, done.Exp);
        Assert.Equal(11250, done.Sp);
    }
}
