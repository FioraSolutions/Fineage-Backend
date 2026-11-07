using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q326 / Q328 from scripts/gen_quests_21_ol_mahum_business.py.</summary>
public sealed class OlMahumBusinessQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q326_vanquish_remnants_sell_and_quit()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 326, Leopold = 30435, Red = 1359, Blue = 1360;
        var me = Any(21);
        Assert.True(m.AdvEvent(j, bag, Q, Leopold, "30435-03", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20053, me); // red
        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20061, me); // blue
        Assert.Equal(5, bag.CountOf(Red));
        Assert.Equal(5, bag.CountOf(Blue));

        Assert.True(m.Talk(j, bag, Q, Leopold, me).Ok);
        Assert.Equal(0, bag.CountOf(Red));
        Assert.Equal(0, bag.CountOf(Blue));
        Assert.Equal(5 * 46 + 5 * 52 + 4320, bag.CountOf(57)); // 10 total → bonus

        Assert.True(m.AdvEvent(j, bag, Q, Leopold, "30435-07", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q328_sense_for_business_sell()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 328, Sarien = 30436, Carcass = 1347, Gizzard = 1348;
        var me = Any(21);
        Assert.True(m.AdvEvent(j, bag, Q, Sarien, "30436-03", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20055, me);
        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20070, me);
        Assert.True(bag.CountOf(Carcass) >= 1);
        Assert.True(bag.CountOf(Gizzard) >= 1);

        int carc = bag.CountOf(Carcass);
        int lens = bag.CountOf(1366);
        int giz = bag.CountOf(Gizzard);
        int total = carc + lens + giz;
        Assert.True(m.Talk(j, bag, Q, Sarien, me).Ok);
        Assert.Equal(0, bag.CountOf(Carcass));
        Assert.Equal(carc * 25 + lens * 1000 + giz * 60 + (total >= 10 ? 618 : 0), bag.CountOf(57));
    }
}
