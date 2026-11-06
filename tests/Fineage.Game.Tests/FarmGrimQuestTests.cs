using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q151 / Q259 / Q325 from scripts/gen_quests_15_20_farm_grim.py.</summary>
public sealed class FarmGrimQuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q151_cure_for_fever_medicine_path()
    {
        var (m, bag, j) = Setup(0.0); // 20% sac always hits
        const int Q = 151, Elias = 30050, Yohanes = 30032, Sac = 703, Med = 704, Shield = 102;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Elias, "30050-03", me).Ok);

        Assert.True(m.OnKill(j, bag, 20103, me).Ok);
        Assert.Equal(1, bag.CountOf(Sac));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.Contains("poison sac", m.Talk(j, bag, Q, Elias, me).Html ?? "");
        Assert.True(m.Talk(j, bag, Q, Yohanes, me).Ok);
        Assert.Equal(0, bag.CountOf(Sac));
        Assert.Equal(1, bag.CountOf(Med));
        Assert.Equal(3, j.Find(Q)!.Cond);

        var done = m.Talk(j, bag, Q, Elias, me);
        Assert.True(done.Ok);
        Assert.Equal(13106, done.Exp);
        Assert.Equal(613, done.Sp);
        Assert.Equal(1, bag.CountOf(Shield));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q259_farm_owner_sell_and_marius_swap()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 259, Edmond = 30497, Marius = 30405, Skin = 1495;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Edmond, "30497-03", me).Ok);

        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20103, me);
        Assert.Equal(10, bag.CountOf(Skin));

        Assert.True(m.Talk(j, bag, Q, Edmond, me).Ok);
        Assert.Equal(0, bag.CountOf(Skin));
        Assert.Equal(10 * 25 + 250, bag.CountOf(57));

        for (int i = 0; i < 10; i++)
            m.OnKill(j, bag, 20106, me);
        Assert.True(m.AdvEvent(j, bag, Q, Marius, "30405-04", me).Ok);
        Assert.Equal(0, bag.CountOf(Skin));
        Assert.Equal(2, bag.CountOf(1061));

        Assert.True(m.AdvEvent(j, bag, Q, Edmond, "30497-06", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q325_grim_collector_assemble_sell_quit()
    {
        var (m, bag, j) = Setup(0.0); // assemble 80% + drops hit
        const int Q = 325, Curtiz = 30336, Varsak = 30342, Samed = 30434;
        const int Diag = 1349, Spine = 1355, Arm = 1356, Skull = 1353, Rib = 1354, Thigh = 1357, Complete = 1358;
        var me = Any(15);
        Assert.True(m.AdvEvent(j, bag, Q, Curtiz, "30336-03", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Samed, "30434-03", me).Ok);
        Assert.Equal(1, bag.CountOf(Diag));

        m.OnKill(j, bag, 20026, me); // head/heart/liver table
        Assert.True(bag.CountOf(1350) + bag.CountOf(1351) + bag.CountOf(1352) >= 1);

        // Force bones for assemble
        bag.Add(Spine, 1);
        bag.Add(Arm, 1);
        bag.Add(Skull, 1);
        bag.Add(Rib, 1);
        bag.Add(Thigh, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Varsak, "assembleSkeleton", me).Ok);
        Assert.Equal(1, bag.CountOf(Complete));
        Assert.Equal(0, bag.CountOf(Spine));

        int before = bag.CountOf(57);
        Assert.True(m.AdvEvent(j, bag, Q, Samed, "30434-09", me).Ok);
        Assert.Equal(0, bag.CountOf(Complete));
        Assert.Equal(before + 341 + 543, bag.CountOf(57));

        Assert.True(m.AdvEvent(j, bag, Q, Samed, "30434-06", me).Ok);
        Assert.Null(j.Find(Q));
    }
}
