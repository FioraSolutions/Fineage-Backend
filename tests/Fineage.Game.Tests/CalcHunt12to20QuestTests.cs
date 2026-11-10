using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q169 / Q316 / Q341 / Q347 from scripts/gen_quests_12_20_calc_hunts.py.</summary>
public sealed class CalcHunt12to20QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);
    static QuestActor DarkElf(int level) => new(2, level, 0);
    static QuestActor Elf(int level) => new(1, level, 0);

    [Fact]
    public void Q169_offspring_perfect_skull_reward()
    {
        var (m, bag, j) = Setup(0.0); // perfect 20% + cracked 50% both hit
        const int Q = 169, Vlasty = 30145, Cracked = 1030, Perfect = 1031, Gaiters = 31;
        var me = DarkElf(15);
        Assert.True(m.AdvEvent(j, bag, Q, Vlasty, "30145-03", me).Ok);

        m.OnKill(j, bag, 20025, me);
        Assert.Equal(1, bag.CountOf(Perfect));
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.True(bag.CountOf(Cracked) >= 1);
        int cracked = bag.CountOf(Cracked);

        var done = m.AdvEvent(j, bag, Q, Vlasty, "30145-07", me);
        Assert.True(done.Ok);
        Assert.Equal(17475, done.Exp);
        Assert.Equal(1, bag.CountOf(Gaiters));
        Assert.Equal(17030 + cracked * 10, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q316_plague_carriers_sell_and_quit()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 316, Ellenia = 30155, Rat = 1042, Varool = 1043;
        var me = Elf(18);
        Assert.True(m.AdvEvent(j, bag, Q, Ellenia, "30155-04", me).Ok);

        for (int i = 0; i < 9; i++)
            m.OnPartyKill(j, bag, Q, 20040, me);
        m.OnPartyKill(j, bag, Q, 27020, me);
        Assert.Equal(9, bag.CountOf(Rat));
        Assert.Equal(1, bag.CountOf(Varool));

        Assert.True(m.Talk(j, bag, Q, Ellenia, me).Ok);
        Assert.Equal(0, bag.CountOf(Rat));
        Assert.Equal(0, bag.CountOf(Varool));
        Assert.Equal(9 * 30 + 10000 + 5000, bag.CountOf(57)); // 10 total → bonus

        Assert.True(m.AdvEvent(j, bag, Q, Ellenia, "30155-08", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q341_wild_beasts_twenty_skins()
    {
        var (m, bag, j) = Setup(0.0); // 83%+ always hits
        const int Q = 341, Pano = 30078, Skin = 4259;
        var me = Any(20);
        Assert.True(m.AdvEvent(j, bag, Q, Pano, "30078-04", me).Ok);
        for (int i = 0; i < 20; i++)
            m.OnKill(j, bag, 20021, me);
        Assert.Equal(20, bag.CountOf(Skin));
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.True(m.Talk(j, bag, Q, Pano, me).Ok);
        Assert.Equal(3710, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q347_calculator_guild_chain()
    {
        var (m, bag, j) = Setup(0.0); // gem 40% hits
        const int Q = 347, Brunon = 30526, Spiron = 30532, Balanki = 30533, Silvera = 30527;
        const int Stolen = 4285, Gem = 4286, Calc = 4393;
        var me = Any(12);
        Assert.True(m.AdvEvent(j, bag, Q, Brunon, "30526-08", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Spiron, "30532-02", me).Ok);
        Assert.Equal(2, j.Find(Q)!.Cond);

        bag.Add(57, 200);
        Assert.True(m.AdvEvent(j, bag, Q, Balanki, "30533-02", me).Ok);
        Assert.Equal(100, bag.CountOf(57));
        Assert.Equal(3, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Silvera, me).Ok); // cond 3 → 4
        Assert.Equal(4, j.Find(Q)!.Cond);

        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20540, me);
        Assert.Equal(10, bag.CountOf(Gem));
        Assert.Equal(5, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Silvera, me).Ok);
        Assert.Equal(0, bag.CountOf(Gem));
        Assert.Equal(1, bag.CountOf(Stolen));
        Assert.Equal(6, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Brunon, "30526-10", me).Ok);
        Assert.Equal(1, bag.CountOf(Calc));
        Assert.Null(j.Find(Q));
    }
}
