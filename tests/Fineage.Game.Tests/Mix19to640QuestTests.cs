using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q19 / Q113 / Q3 / Q640 from scripts/gen_quests_19_640_mix.py.</summary>
public sealed class Mix19to640QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level, int race = 0) => new(race, level, 0);

    [Fact]
    public void Q19_meat_delivery()
    {
        var (m, bag, j) = Setup();
        const int Q = 19, Vlad = 31302, Tuna = 31537, Meat = 7547;
        var me = Any(63);
        Assert.True(m.AdvEvent(j, bag, Q, Vlad, "31302-01", me).Ok);
        Assert.Equal(1, bag.CountOf(Meat));

        Assert.True(m.AdvEvent(j, bag, Q, Tuna, "019_finish", me).Ok);
        Assert.Equal(0, bag.CountOf(Meat));
        Assert.Equal(30000, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q113_beacon_box_delivery()
    {
        var (m, bag, j) = Setup();
        const int Q = 113, Moira = 31979, Torrant = 32016, Box = 8086;
        var me = Any(40);
        Assert.True(m.AdvEvent(j, bag, Q, Moira, "31979-02", me).Ok);
        Assert.Equal(1, bag.CountOf(Box));

        Assert.True(m.AdvEvent(j, bag, Q, Torrant, "32016-02", me).Ok);
        Assert.Equal(0, bag.CountOf(Box));
        Assert.Equal(21578, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q3_dark_elf_triple_collect()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 3, Talloth = 30141;
        const int Eye = 1081, Stone = 1082, Blood = 1083, Scroll = 956;
        var me = Any(16, race: 2);
        Assert.True(m.AdvEvent(j, bag, Q, Talloth, "30141-03", me).Ok);

        m.OnPartyKill(j, bag, Q, 20031, me);
        m.OnPartyKill(j, bag, Q, 20041, me);
        m.OnPartyKill(j, bag, Q, 20048, me);
        Assert.Equal(1, bag.CountOf(Eye));
        Assert.Equal(1, bag.CountOf(Stone));
        Assert.Equal(1, bag.CountOf(Blood));

        Assert.True(m.Talk(j, bag, Q, Talloth, me).Ok);
        Assert.Equal(1, bag.CountOf(Scroll));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q640_fang_enria_trade()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 640, Kahman = 31554, Fang = 8085, Enria = 4042;
        var me = Any(66);
        Assert.True(m.AdvEvent(j, bag, Q, Kahman, "31554-02", me).Ok);

        m.OnPartyKill(j, bag, Q, 22105, me);
        Assert.Equal(10, bag.CountOf(Fang));
        // need 12 for Enria — second kill
        m.OnPartyKill(j, bag, Q, 22105, me);
        Assert.Equal(20, bag.CountOf(Fang));

        Assert.True(m.AdvEvent(j, bag, Q, Kahman, "0", me).Ok);
        Assert.Equal(8, bag.CountOf(Fang));
        Assert.Equal(1, bag.CountOf(Enria));
    }
}
