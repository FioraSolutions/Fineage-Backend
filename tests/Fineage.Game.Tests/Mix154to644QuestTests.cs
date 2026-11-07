using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q154 / Q155 / Q642 / Q644 from scripts/gen_quests_154_644_mix.py.</summary>
public sealed class Mix154to644QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q154_sacrifice_chain()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 154, Rock = 30312, Cristel = 30051, Roll = 30055;
        const int Fur = 1032, Yarn = 1033, Doll = 1034, Ear = 113;
        var me = Any(2);
        Assert.True(m.AdvEvent(j, bag, Q, Rock, "30312-03", me).Ok);

        for (int i = 0; i < 10; i++)
            m.OnPartyKill(j, bag, Q, 20481, me);
        Assert.Equal(10, bag.CountOf(Fur));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Cristel, me).Ok);
        Assert.Equal(0, bag.CountOf(Fur));
        Assert.Equal(1, bag.CountOf(Yarn));
        Assert.Equal(3, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Roll, me).Ok);
        Assert.Equal(1, bag.CountOf(Doll));
        Assert.Equal(4, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Rock, me).Ok);
        Assert.Equal(1, bag.CountOf(Ear));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q155_windawood_delivery()
    {
        var (m, bag, j) = Setup();
        const int Q = 155, Abellos = 30042, Windawood = 30311, Letter = 1019, Haste = 734;
        var me = Any(3);
        Assert.True(m.AdvEvent(j, bag, Q, Abellos, "30042-03", me).Ok);
        Assert.Equal(1, bag.CountOf(Letter));

        Assert.True(m.Talk(j, bag, Q, Windawood, me).Ok);
        Assert.Equal(1, bag.CountOf(Haste));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q642_tissue_sell()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 642, Dinn = 32105, Tissue = 8774;
        var me = Any(75);
        Assert.True(m.AdvEvent(j, bag, Q, Dinn, "32105-05", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnPartyKill(j, bag, Q, 22215, me);
        Assert.Equal(5, bag.CountOf(Tissue));

        Assert.True(m.AdvEvent(j, bag, Q, Dinn, "32105-09", me).Ok);
        Assert.Equal(0, bag.CountOf(Tissue));
        Assert.Equal(5 * 5000, bag.CountOf(57));
    }

    [Fact]
    public void Q644_grave_goods_varnish()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 644, Karuda = 32017, Goods = 8088;
        var me = Any(20);
        Assert.True(m.AdvEvent(j, bag, Q, Karuda, "32017-03", me).Ok);

        for (int i = 0; i < 120; i++)
            m.OnPartyKill(j, bag, Q, 22004, me);
        Assert.Equal(120, bag.CountOf(Goods));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Karuda, "varnish", me).Ok);
        Assert.Null(j.Find(Q));
        Assert.Equal(30, bag.CountOf(1865));
    }
}
