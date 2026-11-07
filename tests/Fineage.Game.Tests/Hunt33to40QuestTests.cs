using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q329 / Q338 from scripts/gen_quests_33_40_hunts.py.</summary>
public sealed class Hunt33to40QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q329_curiosity_sell_and_quit()
    {
        var (m, bag, j) = Setup(0.0); // both exclusive bands hit independently
        const int Q = 329, Rolento = 30437, Golem = 1346, Broken = 1365;
        var me = Any(33);
        Assert.True(m.AdvEvent(j, bag, Q, Rolento, "30437-03", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnPartyKill(j, bag, Q, 20083, me);
        Assert.True(bag.CountOf(Golem) >= 1);
        Assert.True(bag.CountOf(Broken) >= 1);

        int g = bag.CountOf(Golem);
        int b = bag.CountOf(Broken);
        int total = g + b;
        Assert.True(m.Talk(j, bag, Q, Rolento, me).Ok);
        Assert.Equal(0, bag.CountOf(Golem));
        Assert.Equal(0, bag.CountOf(Broken));
        Assert.Equal(b * 50 + g * 1000 + (total >= 10 ? 1183 : 0), bag.CountOf(57));

        Assert.True(m.AdvEvent(j, bag, Q, Rolento, "30437-06", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q338_alligator_event_sell()
    {
        var (m, bag, j) = Setup(0.0); // always + 19% second both hit → 2 per kill
        const int Q = 338, Enverun = 30892, Leather = 4337;
        var me = Any(40);
        Assert.True(m.AdvEvent(j, bag, Q, Enverun, "30892-03", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20135, me);
        Assert.Equal(10, bag.CountOf(Leather)); // 5×2

        Assert.True(m.AdvEvent(j, bag, Q, Enverun, "30892-06", me).Ok);
        Assert.Equal(0, bag.CountOf(Leather));
        Assert.Equal(10 * 60 + 3430, bag.CountOf(57));

        Assert.True(m.AdvEvent(j, bag, Q, Enverun, "30892-10", me).Ok);
        Assert.Null(j.Find(Q));
    }
}
