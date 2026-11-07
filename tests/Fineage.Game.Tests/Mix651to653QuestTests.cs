using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q651 / Q652 / Q653 from scripts/gen_quests_651_653_escorts.py.</summary>
public sealed class Mix651to653QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => 0.0), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q651_runaway_youth()
    {
        var (m, bag, j) = Setup();
        const int Q = 651, Ivan = 32014, Batidae = 31989, Soe = 736;
        var me = Any(26);
        bag.Add(Soe, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Ivan, "32014-04", me).Ok);
        Assert.Equal(0, bag.CountOf(Soe));
        Assert.True(m.Talk(j, bag, Q, Batidae, me).Ok);
        Assert.Equal(2883, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q652_aged_adventurer()
    {
        var (m, bag, j) = Setup();
        const int Q = 652, Tantan = 32012, Sara = 30180, SsC = 1464;
        var me = Any(46);
        bag.Add(SsC, 100);
        Assert.True(m.AdvEvent(j, bag, Q, Tantan, "32012-04", me).Ok);
        Assert.Equal(0, bag.CountOf(SsC));
        Assert.True(m.Talk(j, bag, Q, Sara, me).Ok);
        Assert.Equal(10000, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q653_wild_maiden()
    {
        var (m, bag, j) = Setup();
        const int Q = 653, Suki = 32013, Galibredo = 30181, Soe = 736;
        var me = Any(36);
        bag.Add(Soe, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Suki, "32013-04", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Galibredo, me).Ok);
        Assert.Equal(2553, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }
}
