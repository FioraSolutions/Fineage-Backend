using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q262 / Q303 / Q313 / Q320 from scripts/gen_quests_8_10_collects.py.</summary>
public sealed class Collect8to10QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);
    static QuestActor DarkElf(int level) => new(2, level, 0);

    [Fact]
    public void Q262_ivory_tower_ten_sacs()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 262, Npc = 30137, Sac = 707;
        var me = Any(8);
        Assert.True(m.AdvEvent(j, bag, Q, Npc, "30137-03", me).Ok);
        for (int i = 0; i < 10; i++)
            m.OnPartyKill(j, bag, Q, 20007, me);
        Assert.Equal(10, bag.CountOf(Sac));
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.True(m.Talk(j, bag, Q, Npc, me).Ok);
        Assert.Equal(3000, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q303_arrowheads_ten()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 303, Npc = 30029, Head = 963;
        var me = Any(10);
        Assert.True(m.AdvEvent(j, bag, Q, Npc, "30029-04", me).Ok);
        for (int i = 0; i < 10; i++)
            m.OnPartyKill(j, bag, Q, 20361, me);
        Assert.Equal(10, bag.CountOf(Head));
        var done = m.Talk(j, bag, Q, Npc, me);
        Assert.True(done.Ok);
        Assert.Equal(1000, bag.CountOf(57));
        Assert.Equal(2000, done.Exp);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q313_collect_spores_ten()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 313, Npc = 30150, Sac = 1118;
        var me = Any(8);
        Assert.True(m.AdvEvent(j, bag, Q, Npc, "30150-05", me).Ok);
        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20509, me);
        Assert.Equal(10, bag.CountOf(Sac));
        Assert.Equal(2, j.Find(Q)!.Cond);
        Assert.True(m.Talk(j, bag, Q, Npc, me).Ok);
        Assert.Equal(3500, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q320_bones_tell_the_future()
    {
        var (m, bag, j) = Setup(0.0); // 18% always hits at roll 0
        const int Q = 320, Npc = 30359, Bone = 809;
        var me = DarkElf(10);
        Assert.True(m.AdvEvent(j, bag, Q, Npc, "30359-04", me).Ok);
        for (int i = 0; i < 10; i++)
            m.OnPartyKill(j, bag, Q, 20517, me);
        Assert.Equal(10, bag.CountOf(Bone));
        Assert.True(m.Talk(j, bag, Q, Npc, me).Ok);
        Assert.Equal(8470, bag.CountOf(57));
        Assert.Null(j.Find(Q));
    }
}
