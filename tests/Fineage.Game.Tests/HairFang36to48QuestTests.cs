using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q366 / Q368 from scripts/gen_quests_36_48_hair_fang.py.</summary>
public sealed class HairFang36to48QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q366_silver_hair_sell()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 366, Dieter = 30111, Hair = 5874;
        var me = Any(48);
        Assert.True(m.AdvEvent(j, bag, Q, Dieter, "30111-02", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnPartyKill(j, bag, Q, 20986, me);
        Assert.Equal(5, bag.CountOf(Hair));

        Assert.True(m.Talk(j, bag, Q, Dieter, me).Ok);
        Assert.Equal(0, bag.CountOf(Hair));
        Assert.Equal(5 * 500 + 29000, bag.CountOf(57));

        Assert.True(m.AdvEvent(j, bag, Q, Dieter, "30111-05", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q368_fang_sell_tiers()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 368, Restina = 30926, Fang = 5881;
        var me = Any(36);
        Assert.True(m.AdvEvent(j, bag, Q, Restina, "30926-02", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnPartyKill(j, bag, Q, 20797, me);
        Assert.Equal(5, bag.CountOf(Fang));
        Assert.True(m.Talk(j, bag, Q, Restina, me).Ok);
        Assert.Equal(5 * 250 + 2000, bag.CountOf(57));

        for (int i = 0; i < 10; i++)
            m.OnPartyKill(j, bag, Q, 20797, me);
        Assert.True(m.Talk(j, bag, Q, Restina, me).Ok);
        Assert.Equal(5 * 250 + 2000 + 10 * 250 + 9450, bag.CountOf(57));
    }
}
