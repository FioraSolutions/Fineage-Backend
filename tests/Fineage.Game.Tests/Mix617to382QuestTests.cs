using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q617 / Q382 / Q358 from scripts/gen_quests_617_382_mix.py.</summary>
public sealed class Mix617to382QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q617_vulcan_forgotten_blade()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 617, Vulcan = 31539, Torch = 7264, Blade = 6881;
        var me = Any(74);
        Assert.True(m.AdvEvent(j, bag, Q, Vulcan, "31539-03", me).Ok);

        for (int i = 0; i < 1000; i++)
            m.OnPartyKill(j, bag, Q, 21382, me); // 60% → with roll 0 always drops
        Assert.Equal(1000, bag.CountOf(Torch));

        Assert.True(m.AdvEvent(j, bag, Q, Vulcan, "31539-05", me).Ok);
        Assert.Equal(0, bag.CountOf(Torch));
        Assert.Equal(1, bag.CountOf(Blade));
    }

    [Fact]
    public void Q382_kail_silver_drop()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 382, Vergara = 30687, Royal = 5898, Silver = 5961;
        var me = Any(55);
        bag.Add(Royal, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Vergara, "30687-03", me).Ok);

        m.OnKill(j, bag, 21017, me);
        Assert.Equal(1, bag.CountOf(Silver));
    }

    [Fact]
    public void Q358_snake_scales_recipe()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 358, Oltran = 30862, Scale = 5868, Recipe = 5364;
        var me = Any(63);
        Assert.True(m.AdvEvent(j, bag, Q, Oltran, "30862-04", me).Ok);

        for (int i = 0; i < 108; i++)
            m.OnPartyKill(j, bag, Q, 20672, me);
        Assert.Equal(108, bag.CountOf(Scale));

        Assert.True(m.Talk(j, bag, Q, Oltran, me).Ok);
        Assert.Equal(0, bag.CountOf(Scale));
        Assert.Equal(1, bag.CountOf(Recipe));
        Assert.Null(j.Find(Q));
    }
}
