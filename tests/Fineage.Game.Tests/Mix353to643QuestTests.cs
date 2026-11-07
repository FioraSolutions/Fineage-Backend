using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q353 / Q631 / Q643 from scripts/gen_quests_353_643_mix.py.</summary>
public sealed class Mix353to643QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q353_stone_sell()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 353, Galman = 31044, Stone = 5862;
        var me = Any(55);
        Assert.True(m.AdvEvent(j, bag, Q, Galman, "31044-04", me).Ok);

        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20245, me);
        Assert.Equal(5, bag.CountOf(Stone));

        Assert.True(m.Talk(j, bag, Q, Galman, me).Ok);
        Assert.Equal(0, bag.CountOf(Stone));
        Assert.Equal(2500 + 230 * 5, bag.CountOf(57));
    }

    [Fact]
    public void Q631_meat_mold_glue()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 631, Tuna = 31537, Meat = 7546, Glue = 4039;
        var me = Any(65);
        Assert.True(m.AdvEvent(j, bag, Q, Tuna, "31537-03", me).Ok);

        for (int i = 0; i < 120; i++)
            m.OnPartyKill(j, bag, Q, 21460, me);
        Assert.Equal(120, bag.CountOf(Meat));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Tuna, "0", me).Ok);
        Assert.Equal(0, bag.CountOf(Meat));
        Assert.Equal(15, bag.CountOf(Glue));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q643_bones_sell()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 643, Sing = 32106, Bones = 8776;
        var me = Any(75);
        Assert.True(m.AdvEvent(j, bag, Q, Sing, "quest_accept", me).Ok);

        m.OnPartyKill(j, bag, Q, 22200, me);
        Assert.Equal(8, bag.CountOf(Bones));

        Assert.True(m.AdvEvent(j, bag, Q, Sing, "32106-09", me).Ok);
        Assert.Equal(0, bag.CountOf(Bones));
        Assert.Equal(8 * 1374, bag.CountOf(57));
    }
}
