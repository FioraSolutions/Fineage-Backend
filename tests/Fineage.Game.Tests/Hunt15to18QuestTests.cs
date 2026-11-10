using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q276 / Q277 / Q296 / Q297 / Q324 from scripts/gen_hunts_15_18.py.</summary>
public sealed class Hunt15to18QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);
    static QuestActor Orc(int level) => new(3, level, 44);

    [Fact]
    public void Q276_hestui_totem_via_spawn_kill()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 276, Tanapi = 30571, Para = 1480, Crystal = 1481, Pants = 29, Totem = 1500;
        var me = Orc(15);
        Assert.True(m.AdvEvent(j, bag, Q, Tanapi, "30571-03", me).Ok);

        for (int i = 0; i < 50; i++)
            m.OnKill(j, bag, 20479, me);
        Assert.Equal(50, bag.CountOf(Para));

        // 51st bear kill: spawn totem + take 50 parasites (OnKill aggregate omits Spawns).
        Assert.True(m.OnKill(j, bag, 20479, me).Ok);
        Assert.Equal(0, bag.CountOf(Para));

        m.OnKill(j, bag, 27044, me);
        Assert.Equal(1, bag.CountOf(Crystal));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.Talk(j, bag, Q, Tanapi, me).Ok);
        Assert.Equal(1, bag.CountOf(Pants));
        Assert.Equal(1, bag.CountOf(Totem));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q277_gatekeeper_offering_charms()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 277, Tamil = 30576, Stone = 1572, Charm = 1658;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Tamil, "30576-03", me).Ok);
        for (int i = 0; i < 20; i++)
            m.OnKill(j, bag, 20333, me);
        Assert.Equal(20, bag.CountOf(Stone));
        Assert.True(m.Talk(j, bag, Q, Tamil, me).Ok);
        Assert.Equal(2, bag.CountOf(Charm));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q296_tarantula_silk_sell_and_convert()
    {
        var (m, bag, j) = Setup(0.0); // silk 50% + spin 5% both hit
        const int Q = 296, Mion = 30519, Nathan = 30548, Silk = 1493, Spin = 1494;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Mion, "30519-03", me).Ok);

        for (int i = 0; i < 4; i++)
            m.OnKill(j, bag, 20394, me);
        Assert.True(bag.CountOf(Silk) >= 1);
        Assert.True(bag.CountOf(Spin) >= 1);

        int spin = bag.CountOf(Spin);
        Assert.True(m.AdvEvent(j, bag, Q, Nathan, "30548-03", me).Ok);
        Assert.Equal(0, bag.CountOf(Spin));
        Assert.True(bag.CountOf(Silk) >= spin * 19);

        int before = bag.CountOf(57);
        int silk = bag.CountOf(Silk);
        Assert.True(m.Talk(j, bag, Q, Mion, me).Ok);
        Assert.Equal(0, bag.CountOf(Silk));
        Assert.Equal(before + silk * 30 + (silk >= 10 ? 2000 : 0), bag.CountOf(57));
    }

    [Fact]
    public void Q297_gatekeeper_favor_tokens()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 297, Wirphy = 30540, Token = 1659;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Wirphy, "30540-03", me).Ok);
        for (int i = 0; i < 20; i++)
            m.OnKill(j, bag, 20521, me);
        Assert.True(m.Talk(j, bag, Q, Wirphy, me).Ok);
        Assert.Equal(2, bag.CountOf(Token));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q324_sweetest_venom_ten_sacs()
    {
        var (m, bag, j) = Setup(0.0); // 26%+ always hits
        const int Q = 324, Astaron = 30351, Sac = 1077;
        var me = Any(18);
        Assert.True(m.AdvEvent(j, bag, Q, Astaron, "30351-04", me).Ok);
        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20034, me);
        Assert.Equal(10, bag.CountOf(Sac));
        Assert.True(m.Talk(j, bag, Q, Astaron, me).Ok);
        Assert.Equal(5810, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }
}
