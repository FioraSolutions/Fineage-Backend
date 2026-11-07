using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q636 / Q379 / Q645 / Q647 from scripts/gen_quests_636_647_mix.py.</summary>
public sealed class Mix636to647QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q636_visitor_mark_delivery()
    {
        var (m, bag, j) = Setup();
        const int Q = 636, Eliyah = 31329, Flauron = 32010, Visitor = 8064;
        var me = Any(73);
        Assert.True(m.AdvEvent(j, bag, Q, Eliyah, "31329-04", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Flauron, "32010-02", me).Ok);
        Assert.Equal(1, bag.CountOf(Visitor));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q379_fantasy_wine_30yr()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 379, Harlan = 30074, Leaf = 5893, Stone = 5894, Wine30 = 5957;
        var me = Any(20);
        Assert.True(m.AdvEvent(j, bag, Q, Harlan, "30074-04", me).Ok);

        for (int i = 0; i < 80; i++)
            m.OnKill(j, bag, 20291, me);
        for (int i = 0; i < 100; i++)
            m.OnKill(j, bag, 20292, me);
        Assert.Equal(80, bag.CountOf(Leaf));
        Assert.Equal(100, bag.CountOf(Stone));

        Assert.True(m.AdvEvent(j, bag, Q, Harlan, "30074-11", me).Ok);
        Assert.Equal(0, bag.CountOf(Leaf));
        Assert.Equal(1, bag.CountOf(Wine30));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q645_grave_goods_hemp()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 645, Karuda = 32017, Goods = 8089, Hemp = 1878;
        var me = Any(23);
        Assert.True(m.AdvEvent(j, bag, Q, Karuda, "32017-03", me).Ok);

        for (int i = 0; i < 500; i++)
            m.OnPartyKill(j, bag, Q, 22007, me);
        Assert.Equal(500, bag.CountOf(Goods));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Karuda, "0", me).Ok);
        Assert.Equal(0, bag.CountOf(Goods));
        Assert.Equal(18, bag.CountOf(Hemp));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q647_golem_shards_adena()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 647, Guten = 32069, Shard = 8100;
        var me = Any(46);
        Assert.True(m.AdvEvent(j, bag, Q, Guten, "32069-02", me).Ok);

        for (int i = 0; i < 500; i++)
            m.OnPartyKill(j, bag, Q, 22052, me);
        Assert.Equal(500, bag.CountOf(Shard));

        Assert.True(m.AdvEvent(j, bag, Q, Guten, "32069-06", me).Ok);
        Assert.Equal(0, bag.CountOf(Shard));
        Assert.Equal(180000, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }
}
