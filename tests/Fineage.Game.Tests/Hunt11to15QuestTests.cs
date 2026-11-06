using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q261 / Q275 / Q295 / Q319 from scripts/gen_hunts_11_15.py.</summary>
public sealed class Hunt11to15QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);
    static QuestActor Orc(int level) => new(3, level, 44);

    [Fact]
    public void Q261_collectors_dream_eight_legs()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 261, Npc = 30222, Leg = 1087;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Npc, "30222-03", me).Ok);
        for (int i = 0; i < 8; i++)
            m.OnKill(j, bag, 20308, me);
        Assert.Equal(8, bag.CountOf(Leg));
        Assert.Equal(2, j.Find(Q)!.Cond);
        var done = m.Talk(j, bag, Q, Npc, me);
        Assert.True(done.Ok);
        Assert.Equal(2000, done.Exp);
        Assert.Equal(1000, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q275_dark_winged_spies_seventy_fangs()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 275, Tantus = 30567, Fang = 1478;
        var me = Orc(11);
        Assert.True(m.AdvEvent(j, bag, Q, Tantus, "30567-03", me).Ok);
        for (int i = 0; i < 70; i++)
            m.OnKill(j, bag, 20316, me);
        Assert.Equal(70, bag.CountOf(Fang));
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.True(m.Talk(j, bag, Q, Tantus, me).Ok);
        Assert.Equal(0, bag.CountOf(Fang));
        Assert.Equal(70 * 60, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q295_dreaming_of_the_skies_ring()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 295, Arin = 30536, Stone = 1492, Ring = 1509;
        var me = Any(11);
        Assert.True(m.AdvEvent(j, bag, Q, Arin, "30536-03", me).Ok);
        // roll 0 → base + bonus stone each kill; 25 kills ×2 = 50
        for (int i = 0; i < 25; i++)
            m.OnKill(j, bag, 20153, me);
        Assert.Equal(50, bag.CountOf(Stone));
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.True(m.Talk(j, bag, Q, Arin, me).Ok);
        Assert.Equal(1, bag.CountOf(Ring));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q319_scent_of_death_five_skins()
    {
        var (m, bag, j) = Setup(0.0); // 20% always hits
        const int Q = 319, Npc = 30138, Skin = 1045, Pot = 1060;
        var me = Any(11);
        Assert.True(m.AdvEvent(j, bag, Q, Npc, "30138-04", me).Ok);
        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 20015, me);
        Assert.Equal(5, bag.CountOf(Skin));
        Assert.True(m.Talk(j, bag, Q, Npc, me).Ok);
        Assert.Equal(3350, bag.CountOf(57));
        Assert.Equal(1, bag.CountOf(Pot));
        Assert.Null(j.Find(Q));
    }
}
