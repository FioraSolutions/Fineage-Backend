using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q354 / Q357 from scripts/gen_quests_38_47_island.py.</summary>
public sealed class Island38to47QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q354_teeth_sell_tiers_and_map()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 354, Kluck = 30895, Tooth = 5863, Piece = 5864, Map = 5915;
        var me = Any(38);
        Assert.True(m.AdvEvent(j, bag, Q, Kluck, "30895-02", me).Ok);

        // MOB2 always drops tooth (+ second at roll 0) + map piece
        for (int i = 0; i < 5; i++)
            m.OnPartyKill(j, bag, Q, 20808, me);
        Assert.Equal(10, bag.CountOf(Tooth)); // 5×2
        Assert.Equal(5, bag.CountOf(Piece));

        // Low tier sell (<100)
        Assert.True(m.AdvEvent(j, bag, Q, Kluck, "ADENA", me).Ok);
        Assert.Equal(0, bag.CountOf(Tooth));
        Assert.Equal(10 * 220 + 3100, bag.CountOf(57));

        // Exchange 10 pieces → treasure map
        for (int i = 0; i < 5; i++)
            m.OnPartyKill(j, bag, Q, 20808, me); // +5 pieces → 10 total
        Assert.Equal(10, bag.CountOf(Piece));
        Assert.True(m.AdvEvent(j, bag, Q, Kluck, "REWARD", me).Ok);
        Assert.Equal(0, bag.CountOf(Piece));
        Assert.Equal(1, bag.CountOf(Map));

        Assert.True(m.AdvEvent(j, bag, Q, Kluck, "30895-10", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q354_high_tier_sell()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 354, Kluck = 30895, Tooth = 5863;
        var me = Any(38);
        Assert.True(m.AdvEvent(j, bag, Q, Kluck, "30895-02", me).Ok);
        for (int i = 0; i < 50; i++)
            m.OnPartyKill(j, bag, Q, 20808, me); // 100 teeth
        Assert.Equal(100, bag.CountOf(Tooth));
        Assert.True(m.AdvEvent(j, bag, Q, Kluck, "ADENA", me).Ok);
        Assert.Equal(100 * 220 + 10700, bag.CountOf(57));
    }

    [Fact]
    public void Q357_jade_sell_and_quit()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 357, Silva = 30686, Jade = 5867;
        var me = Any(47);
        Assert.True(m.AdvEvent(j, bag, Q, Silva, "30686-05", me).Ok);

        for (int i = 0; i < 10; i++)
            m.OnPartyKill(j, bag, Q, 20595, me); // 60% → always at roll 0
        Assert.Equal(10, bag.CountOf(Jade));

        Assert.True(m.AdvEvent(j, bag, Q, Silva, "30686-09", me).Ok);
        Assert.Equal(0, bag.CountOf(Jade));
        Assert.Equal(10 * 425 + 13500, bag.CountOf(57));

        for (int i = 0; i < 5; i++)
            m.OnPartyKill(j, bag, Q, 20595, me);
        Assert.True(m.AdvEvent(j, bag, Q, Silva, "30686-11", me).Ok);
        Assert.Null(j.Find(Q));
        Assert.Equal(10 * 425 + 13500 + 5 * 425, bag.CountOf(57)); // quit <100 → no small bonus
    }
}
