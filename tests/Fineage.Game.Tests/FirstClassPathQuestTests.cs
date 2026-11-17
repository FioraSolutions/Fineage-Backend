using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Block H1: hand-written 404 / 411 / 414 / 417 path packs plus the engine gates they need
/// (dialog chance, varAtLeast / varBelow, add, take_all).</summary>
public sealed class FirstClassPathQuestTests
{
    static readonly GameData Data = GameData.LoadDefault();

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0) =>
        (new QuestMachine(Data.Quests, Data.Items, () => roll), new PlayerInventory(Data.Items), new QuestJournal());

    [Theory]
    [InlineData(404, 30391, "30391-08", 10, 1292)]
    [InlineData(405, 30022, "30022-05", 10, 1201)]
    [InlineData(411, 30416, "30416-05", 31, 1252)]
    [InlineData(412, 30421, "30421-05", 38, 1261)]
    [InlineData(413, 30330, "30330-06", 38, 1270)]
    [InlineData(414, 30570, "30570-05", 44, 1592)]
    [InlineData(417, 30524, "30524-05", 53, 1642)]
    public void Path_starts_only_for_base_class_at_19_without_the_mark(int q, int npc, string ev, int cls, int mark)
    {
        var (m, bag, j) = Setup();
        m.AdvEvent(j, bag, q, npc, ev, new QuestActor(0, 18, cls));
        Assert.Null(j.Find(q));
        m.AdvEvent(j, bag, q, npc, ev, new QuestActor(0, 20, cls == 10 ? 31 : 10));
        Assert.Null(j.Find(q));
        var held = bag.Add(mark, 1);
        m.AdvEvent(j, bag, q, npc, ev, new QuestActor(0, 20, cls));
        Assert.Null(j.Find(q));
        bag.Remove(held);
        Assert.True(m.AdvEvent(j, bag, q, npc, ev, new QuestActor(0, 20, cls)).Ok);
        Assert.Equal(1, j.Find(q)!.Cond);
    }

    [Fact]
    public void Human_wizard_path_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        var me = new QuestActor(0, 19, 10);
        Assert.True(m.AdvEvent(j, bag, 404, 30391, "30391-08", me).Ok);
        m.Talk(j, bag, 404, 30411, me);
        m.OnKill(j, bag, 20359, me);
        m.Talk(j, bag, 404, 30411, me);
        Assert.Equal(1, bag.CountOf(1282));
        m.Talk(j, bag, 404, 30412, me);
        Assert.True(m.AdvEvent(j, bag, 404, 30410, "30410-03", me).Ok);
        m.Talk(j, bag, 404, 30412, me);
        Assert.Equal(1, bag.CountOf(1285));
        m.Talk(j, bag, 404, 30413, me);
        m.OnKill(j, bag, 27030, me);
        m.OnKill(j, bag, 27030, me);
        Assert.Equal(9, j.Find(404)!.Cond);
        m.Talk(j, bag, 404, 30413, me);
        Assert.Equal(0, bag.CountOf(1287));
        m.Talk(j, bag, 404, 30409, me);
        m.OnKill(j, bag, 20021, me);
        m.Talk(j, bag, 404, 30409, me);
        Assert.Equal(13, j.Find(404)!.Cond);
        var done = m.Talk(j, bag, 404, 30391, me);
        Assert.Equal(3200, done.Exp);
        Assert.Equal(1, bag.CountOf(1292));
        Assert.Equal(0, bag.CountOf(1291));
        Assert.Null(j.Find(404)); // repeatable path: state cleared, Bead keeps it from restarting
    }

    [Fact]
    public void Assassin_path_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        var me = new QuestActor(0, 19, 31);
        m.AdvEvent(j, bag, 411, 30416, "30416-05", me);
        m.AdvEvent(j, bag, 411, 30419, "30419-05", me);
        m.AdvEvent(j, bag, 411, 30382, "30382-03", me);
        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20369, me);
        Assert.Equal(4, j.Find(411)!.Cond);
        m.Talk(j, bag, 411, 30382, me);
        Assert.Equal(0, bag.CountOf(1248));
        m.OnKill(j, bag, 27036, me);
        m.Talk(j, bag, 411, 30419, me);
        Assert.Equal(1, bag.CountOf(1251));
        Assert.Equal(3930, m.Talk(j, bag, 411, 30416, me).Sp);
        Assert.Equal(1, bag.CountOf(1252));
    }

    [Fact]
    public void Orc_raider_green_blood_raises_the_leader_by_chance()
    {
        var (m, bag, j) = Setup(roll: 0.99);
        var me = new QuestActor(0, 19, 44);
        m.AdvEvent(j, bag, 414, 30570, "30570-05", me);
        for (int i = 0; i < 5; i++)
            Assert.Empty(m.OnKill(j, bag, 20320, me).Spawns);
        Assert.Equal(5, bag.CountOf(1578)); // misses collect blood
        var (m0, _, _) = Setup();
        Assert.Equal([27045], m0.OnKill(j, bag, 20320, me).Spawns);
        Assert.Equal(0, bag.CountOf(1578));
        for (int i = 0; i < 10; i++)
            m0.OnKill(j, bag, 27045, me);
        Assert.Equal(2, j.Find(414)!.Cond);
        m0.AdvEvent(j, bag, 414, 30570, "30570-07b", me);
        m0.AdvEvent(j, bag, 414, 31978, "31978-03", me);
        m0.OnKill(j, bag, 27320, me);
        Assert.Equal(2360, m0.Talk(j, bag, 414, 31978, me).Sp);
        Assert.Equal(1, bag.CountOf(1592));
    }

    [Fact]
    public void Scavenger_path_counts_errands_and_bear_steps()
    {
        var (m, bag, j) = Setup();
        var me = new QuestActor(0, 19, 53);
        m.AdvEvent(j, bag, 417, 30524, "30524-05", me);
        Assert.True(m.AdvEvent(j, bag, 417, 30519, "30519_1", me).Ok);
        Assert.Equal(1, bag.CountOf(1649));                   // roll 0 -> Zimenf's potion
        Assert.True(m.AdvEvent(j, bag, 417, 30519, "30519-07", me).Ok);   // id 1
        Assert.True(m.Talk(j, bag, 417, 30538, me).Ok);                 // id 11, pay
        Assert.Equal(1, bag.CountOf(1652));
        m.AdvEvent(j, bag, 417, 30519, "30519_2", me);         // id 11, new potion
        m.Talk(j, bag, 417, 30538, me);                        // id 21
        Assert.Equal(2, j.Find(417)!.Cond);
        m.AdvEvent(j, bag, 417, 30519, "30519_2", me);
        m.Talk(j, bag, 417, 30538, me);                        // delivered at id >= 20 -> cond 3, id 31
        Assert.Equal(3, j.Find(417)!.Cond);
        m.AdvEvent(j, bag, 417, 30519, "30519_2", me);
        m.Talk(j, bag, 417, 30538, me);                        // id 41
        m.AdvEvent(j, bag, 417, 30519, "30519_2", me);
        m.Talk(j, bag, 417, 30538, me);                        // id 51
        m.Talk(j, bag, 417, 30519, me);                        // pay + id >= 50 -> Mion letter, cond 4
        Assert.Equal(4, j.Find(417)!.Cond);
        Assert.Equal(1, bag.CountOf(1646));
        Assert.Equal(0, bag.CountOf(1652));
        m.Talk(j, bag, 417, 30556, me);
        for (int i = 0; i < 21; i++)
            Assert.Empty(m.OnKill(j, bag, 20777, me).Spawns);
        Assert.Equal([27058], m.OnKill(j, bag, 20777, me).Spawns);
        for (int i = 0; i < 5; i++)
            m.OnKill(j, bag, 27058, me);
        m.Talk(j, bag, 417, 30556, me);
        for (int i = 0; i < 20; i++)
            m.OnKill(j, bag, 20508, me);
        Assert.Equal(8, j.Find(417)!.Cond);
        m.AdvEvent(j, bag, 417, 30556, "30556-06b", me);
        Assert.Equal(7080, m.AdvEvent(j, bag, 417, 31958, "31958-02", me).Sp);
        Assert.Equal(1, bag.CountOf(1642));
        Assert.Null(j.Find(417));
    }
}
