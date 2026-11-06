using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q156 / Q167 / Q306 / Q317 from scripts/gen_quests_15_18_delivery_hunts.py.</summary>
public sealed class DeliveryHunt15to18QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q156_millennium_love_diary_path()
    {
        var (m, bag, j) = Setup();
        const int Q = 156, Lilith = 30368, Baenedes = 30369, Letter = 1022, Diary = 1023, Soul = 5250;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Lilith, "30368-05", me).Ok);
        Assert.Equal(1, bag.CountOf(Letter));

        Assert.True(m.AdvEvent(j, bag, Q, Baenedes, "30369-02", me).Ok);
        Assert.Equal(0, bag.CountOf(Letter));
        Assert.Equal(1, bag.CountOf(Diary));
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Lilith, me);
        Assert.True(done.Ok);
        Assert.Equal(3000, done.Exp);
        Assert.Equal(1, bag.CountOf(Soul));
        Assert.Equal(0, bag.CountOf(Diary));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q156_millennium_love_baenedes_early_exit()
    {
        var (m, bag, j) = Setup();
        const int Q = 156, Lilith = 30368, Baenedes = 30369;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Lilith, "30368-05", me).Ok);
        var done = m.AdvEvent(j, bag, Q, Baenedes, "30369-03", me);
        Assert.True(done.Ok);
        Assert.Equal(3000, done.Exp);
        Assert.Equal(0, bag.CountOf(5250));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q167_dwarven_kinship_full_route()
    {
        var (m, bag, j) = Setup();
        const int Q = 167, Carlon = 30350, Haprock = 30255, Norman = 30210;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Carlon, "30350-03", me).Ok);
        Assert.Equal(1, bag.CountOf(1076));

        Assert.True(m.AdvEvent(j, bag, Q, Haprock, "30255-03", me).Ok);
        Assert.Equal(0, bag.CountOf(1076));
        Assert.Equal(1, bag.CountOf(1106));
        Assert.Equal(2000, bag.CountOf(57));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Norman, "30210-02", me).Ok);
        Assert.Equal(22000, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q167_dwarven_kinship_haprock_early_exit()
    {
        var (m, bag, j) = Setup();
        const int Q = 167, Carlon = 30350, Haprock = 30255;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Carlon, "30350-03", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Haprock, "30255-04", me).Ok);
        Assert.Equal(15000, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q306_crystals_sell_and_quit()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 306, Katerina = 30004, Flame = 1020, Ice = 1021;
        var me = Any(17);
        Assert.True(m.AdvEvent(j, bag, Q, Katerina, "30004-04", me).Ok);

        m.OnKill(j, bag, 20109, me); // flame
        m.OnKill(j, bag, 20110, me); // ice
        Assert.Equal(1, bag.CountOf(Flame));
        Assert.Equal(1, bag.CountOf(Ice));

        Assert.True(m.Talk(j, bag, Q, Katerina, me).Ok);
        Assert.Equal(0, bag.CountOf(Flame));
        Assert.Equal(0, bag.CountOf(Ice));
        Assert.Equal(80, bag.CountOf(57)); // 2×40, no bonus (<10)

        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20109, me);
        Assert.Equal(10, bag.CountOf(Flame));
        Assert.True(m.Talk(j, bag, Q, Katerina, me).Ok);
        Assert.Equal(80 + 10 * 40 + 5000, bag.CountOf(57));

        Assert.True(m.AdvEvent(j, bag, Q, Katerina, "30004-08", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q317_catch_the_wind_sell_continue_then_quit()
    {
        var (m, bag, j) = Setup(0.0); // 50% always hits
        const int Q = 317, Rizraell = 30361, Shard = 1078;
        var me = Any(18);
        Assert.True(m.AdvEvent(j, bag, Q, Rizraell, "30361-04", me).Ok);

        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20036, me);
        Assert.Equal(10, bag.CountOf(Shard));

        Assert.True(m.Talk(j, bag, Q, Rizraell, me).Ok); // 07 — no sell yet
        Assert.Equal(10, bag.CountOf(Shard));

        Assert.True(m.AdvEvent(j, bag, Q, Rizraell, "30361-09", me).Ok); // sell + continue
        Assert.Equal(0, bag.CountOf(Shard));
        Assert.Equal(10 * 40 + 2988, bag.CountOf(57));
        Assert.NotNull(j.Find(Q));

        m.OnKill(j, bag, 20044, me);
        Assert.Equal(1, bag.CountOf(Shard));
        Assert.True(m.AdvEvent(j, bag, Q, Rizraell, "30361-08", me).Ok); // sell + quit
        Assert.Equal(0, bag.CountOf(Shard));
        Assert.Equal(10 * 40 + 2988 + 40, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }
}
