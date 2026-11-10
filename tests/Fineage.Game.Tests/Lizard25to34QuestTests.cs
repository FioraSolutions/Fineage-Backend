using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q298 / Q300 from scripts/gen_quests_25_34_lizards.py.</summary>
public sealed class Lizard25to34QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q298_dual_gems_to_sp()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 298, Praga = 30333, Rohmer = 30344, Report = 7182, Gem = 7183, Red = 7184;
        var me = Any(25);
        Assert.True(m.AdvEvent(j, bag, Q, Praga, "30333-03", me).Ok);
        Assert.Equal(1, bag.CountOf(Report));
        Assert.Equal(1, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Rohmer, "30344-04", me).Ok);
        Assert.Equal(0, bag.CountOf(Report));
        Assert.Equal(2, j.Find(Q)!.Cond);

        for (int i = 0; i < 50; i++)
            m.OnPartyKill(j, bag, Q, 20922, me); // gem
        for (int i = 0; i < 50; i++)
            m.OnPartyKill(j, bag, Q, 20926, me); // red
        Assert.Equal(50, bag.CountOf(Gem));
        Assert.Equal(50, bag.CountOf(Red));

        Assert.True(m.Talk(j, bag, Q, Rohmer, me).Ok);
        Assert.Equal(3, j.Find(Q)!.Cond);

        var done = m.AdvEvent(j, bag, Q, Rohmer, "30344-06", me);
        Assert.True(done.Ok);
        Assert.Equal(42000, done.Sp);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q300_sixty_bracelets_adena()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 300, Rath = 30126, Bracelet = 7139;
        var me = Any(34);
        Assert.True(m.AdvEvent(j, bag, Q, Rath, "30126-03", me).Ok);

        for (int i = 0; i < 60; i++)
            m.OnPartyKill(j, bag, Q, 20582, me); // 89%
        Assert.Equal(60, bag.CountOf(Bracelet));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.AdvEvent(j, bag, Q, Rath, "30126-06", me);
        Assert.True(done.Ok);
        Assert.Equal(30000, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }
}
