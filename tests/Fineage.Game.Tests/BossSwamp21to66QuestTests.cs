using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q158 / Q626 / Q629 from scripts/gen_quests_21_66_boss_swamp.py.</summary>
public sealed class BossSwamp21to66QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q158_seed_of_evil()
    {
        var (m, bag, j) = Setup();
        const int Q = 158, Biotin = 30031, Tablet = 1025, SoeD = 956;
        var me = Any(21);
        Assert.True(m.AdvEvent(j, bag, Q, Biotin, "30031-03", me).Ok);

        m.OnKill(j, bag, 27016, me);
        Assert.Equal(1, bag.CountOf(Tablet));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Biotin, me).Ok);
        Assert.Equal(1, bag.CountOf(SoeD));
        Assert.Equal(1495, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q626_dark_twilight_adena()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 626, Hierarch = 31517, Blood = 7169;
        var me = Any(60);
        Assert.True(m.AdvEvent(j, bag, Q, Hierarch, "31517-02", me).Ok);

        for (int i = 0; i < 300; i++)
            m.OnPartyKill(j, bag, Q, 21540, me); // 88%
        Assert.Equal(300, bag.CountOf(Blood));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Hierarch, "Adena", me).Ok);
        Assert.Null(j.Find(Q));
        Assert.Equal(100000, bag.CountOf(57));
    }

    [Fact]
    public void Q629_talon_exchange()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 629, Pierce = 31553, Talon = 7250, Coin = 7251;
        var me = Any(66);
        Assert.True(m.AdvEvent(j, bag, Q, Pierce, "31553-03", me).Ok);

        for (int i = 0; i < 100; i++)
            m.OnPartyKill(j, bag, Q, 21517, me); // 99.9%
        Assert.Equal(100, bag.CountOf(Talon));

        Assert.True(m.AdvEvent(j, bag, Q, Pierce, "31553-07", me).Ok);
        Assert.Equal(0, bag.CountOf(Talon));
        Assert.Equal(20, bag.CountOf(Coin));
        Assert.NotNull(j.Find(Q)); // still running

        Assert.True(m.AdvEvent(j, bag, Q, Pierce, "31553-09", me).Ok);
        Assert.Null(j.Find(Q));
    }
}
