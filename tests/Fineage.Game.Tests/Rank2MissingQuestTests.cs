using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q385 / Q426 / Q505 / Q646 from scripts/gen_quests_rank2_missing.py.</summary>
public sealed class Rank2MissingQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q385_scroll_to_blank_exchange()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 385, Gate = 31095, Ancient = 5902, Blank = 5965;
        var me = Any(25);
        Assert.True(m.AdvEvent(j, bag, Q, Gate, "05", me).Ok);
        Assert.NotNull(j.Find(Q));

        m.OnPartyKill(j, bag, Q, 21208, me);
        Assert.True(bag.CountOf(Ancient) >= 1);

        var turn = m.Talk(j, bag, Q, Gate, me);
        Assert.True(turn.Ok);
        Assert.Equal(0, bag.CountOf(Ancient));
        Assert.True(bag.CountOf(Blank) >= 1);
        Assert.NotNull(j.Find(Q)); // stays started until quit
    }

    [Fact]
    public void Q426_fisher_sweet_fluid_drop()
    {
        var (m, bag, j) = Setup(0.04);
        const int Q = 426, Fisher = 31562, Fluid = 7586;
        var me = Any(20);
        Assert.True(m.AdvEvent(j, bag, Q, Fisher, "03", me).Ok);
        Assert.NotNull(j.Find(Q));

        m.OnPartyKill(j, bag, Q, 20005, me);
        Assert.True(bag.CountOf(Fluid) >= 1);
    }


    [Fact]
    public void Q646_cursed_dolls_steel()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 646, Torrant = 32016, Doll = 8087, Steel = 1880;
        var me = Any(40);
        Assert.True(m.AdvEvent(j, bag, Q, Torrant, "32016-03", me).Ok);

        for (int i = 0; i < 180; i++)
            m.OnPartyKill(j, bag, Q, 22029, me);
        Assert.Equal(180, bag.CountOf(Doll));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Torrant, "0", me).Ok);
        Assert.Equal(0, bag.CountOf(Doll));
        Assert.Equal(9, bag.CountOf(Steel));
        Assert.Null(j.Find(Q));
    }
}