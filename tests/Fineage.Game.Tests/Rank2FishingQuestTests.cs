using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q50–Q53 / Q27–Q30 from scripts/gen_quests_rank2_fishing.py.</summary>
public sealed class Rank2FishingQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q50_essence_drop_and_lure_turn_in()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 50, Lanosco = 31570, Essence = 7621, Lure = 7610, Mob = 21026;
        var me = Any(30);
        Assert.True(m.AdvEvent(j, bag, Q, Lanosco, "31570-03", me).Ok);
        Assert.NotNull(j.Find(Q));

        for (var i = 0; i < 400 && bag.CountOf(Essence) < 100; i++)
            m.OnPartyKill(j, bag, Q, Mob, me);
        Assert.Equal(100, bag.CountOf(Essence));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Lanosco, "31570-07", me).Ok);
        Assert.Equal(0, bag.CountOf(Essence));
        Assert.Equal(4, bag.CountOf(Lure));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q27_chest_to_ring()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 27, Lanosco = 31570, Shaling = 31434, Chest = 6500, Blueprint = 7625, Ring = 880;
        var me = Any(30);
        Assert.True(m.AdvEvent(j, bag, Q, Lanosco, "31570-04", me).Ok);
        bag.AddPieces(Chest, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Lanosco, "31570-07", me).Ok);
        Assert.Equal(0, bag.CountOf(Chest));
        Assert.Equal(1, bag.CountOf(Blueprint));

        Assert.True(m.AdvEvent(j, bag, Q, Shaling, "31434-02", me).Ok);
        Assert.Equal(0, bag.CountOf(Blueprint));
        Assert.Equal(1, bag.CountOf(Ring));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }
}