using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q360 / Q369 from scripts/gen_quests_52_60_plunder_jewels.py.</summary>
public sealed class PlunderJewels52to60QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q360_supply_sell_and_finish()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 360, Coleman = 30873, Supply = 5872;
        var me = Any(52);
        Assert.True(m.AdvEvent(j, bag, Q, Coleman, "30873-04", me).Ok);

        for (int i = 0; i < 4; i++)
            m.OnKill(j, bag, 20669, me); // supply only (pieces <5)
        Assert.Equal(4, bag.CountOf(Supply));
        Assert.True(m.Talk(j, bag, Q, Coleman, me).Ok);
        Assert.Equal(0, bag.CountOf(Supply));
        Assert.Equal(4 * 100 + 6000, bag.CountOf(57));

        Assert.True(m.AdvEvent(j, bag, Q, Coleman, "30873-10", me).Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase); // Mobius one-shot
    }

    [Fact]
    public void Q369_two_phase_jewels()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 369, Nell = 30376, Flare = 5882, Freeze = 5883;
        var me = Any(25);
        Assert.True(m.AdvEvent(j, bag, Q, Nell, "30376-02", me).Ok);
        Assert.Equal("1", j.Find(Q)!.Get("memo"));

        // Phase 1: death fire / roxide always drop 2
        for (int i = 0; i < 25; i++)
            m.OnPartyKill(j, bag, Q, 20749, me);
        for (int i = 0; i < 25; i++)
            m.OnPartyKill(j, bag, Q, 20747, me);
        Assert.Equal(50, bag.CountOf(Flare));
        Assert.Equal(50, bag.CountOf(Freeze));

        Assert.True(m.Talk(j, bag, Q, Nell, me).Ok);
        Assert.Equal(0, bag.CountOf(Flare));
        Assert.Equal(31810, bag.CountOf(57));
        Assert.Equal("2", j.Find(Q)!.Get("memo"));

        Assert.True(m.AdvEvent(j, bag, Q, Nell, "30376-06", me).Ok);
        Assert.Equal("3", j.Find(Q)!.Get("memo"));

        for (int i = 0; i < 100; i++)
            m.OnPartyKill(j, bag, Q, 20749, me);
        for (int i = 0; i < 100; i++)
            m.OnPartyKill(j, bag, Q, 20747, me);
        Assert.Equal(200, bag.CountOf(Flare));
        Assert.Equal(200, bag.CountOf(Freeze));

        Assert.True(m.Talk(j, bag, Q, Nell, me).Ok);
        Assert.Null(j.Find(Q));
        Assert.Equal(31810 + 84415, bag.CountOf(57));
    }
}
