using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q255 / Q431 / Q637 / Q374 / Q375 / Q377 from scripts/gen_quests_stubs_batch1.py.</summary>
public sealed class QuestStubsBatch1Tests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q255_tutorial_pack_loads_with_start()
    {
        var data = GameData.LoadDefault();
        var q = data.Quests.Quests.FirstOrDefault(x => x.Id == 255);
        Assert.NotNull(q);
        Assert.Contains(q!.Dialogs, d => d.Ops.Any(o => o is CreateOp));
    }

    [Fact]
    public void Q431_wedding_march_silver_crystals()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 431, Kantabilon = 31042, Silver = 7540, Echo = 7062;
        var me = Any(40);
        Assert.True(m.AdvEvent(j, bag, Q, Kantabilon, "31042-02", me).Ok);
        Assert.NotNull(j.Find(Q));

        for (int i = 0; i < 80; i++)
            m.OnPartyKill(j, bag, Q, 20786, me);
        if (bag.CountOf(Silver) < 50)
            bag.Add(Silver, 50 - bag.CountOf(Silver));

        Assert.True(m.Talk(j, bag, Q, Kantabilon, me).Ok);
        Assert.Equal(0, bag.CountOf(Silver));
        Assert.True(bag.CountOf(Echo) >= 25);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q637_visitor_mark_hearts()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 637, Flauron = 32010, Faded = 8065, Heart = 8066, Pagan = 8067;
        var me = Any(75);
        bag.Add(Faded, 1);

        Assert.True(m.AdvEvent(j, bag, Q, Flauron, "32010-04", me).Ok);
        for (int i = 0; i < 30; i++)
            m.OnPartyKill(j, bag, Q, 21565, me);
        if (bag.CountOf(Heart) < 10)
            bag.Add(Heart, 10 - bag.CountOf(Heart));

        Assert.True(m.Talk(j, bag, Q, Flauron, me).Ok);
        Assert.True(bag.CountOf(Pagan) >= 1);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q374_starts_and_accepts_material_turn_in()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 374, Manakia = 30515, Tooth = 5884, Light = 5885;
        var me = Any(58);
        Assert.True(m.AdvEvent(j, bag, Q, Manakia, "30515-03", me).Ok);
        bag.Add(Tooth, 65);
        bag.Add(Light, 65);
        Assert.True(m.AdvEvent(j, bag, Q, Manakia, "30515-06-0", me).Ok);
        Assert.Equal(0, bag.CountOf(Tooth));
        Assert.True(bag.CountOf(5486) >= 3);
    }

    [Fact]
    public void Q375_requires_mysterious_stone()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 375, Manakia = 30515, Stone = 5887;
        var me = Any(62);
        bag.Add(Stone, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Manakia, "30515-03", me).Ok);
        Assert.Equal(0, bag.CountOf(Stone));
    }

    [Fact]
    public void Q377_starts_with_dictionary()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 377, Sobling = 31147;
        var me = Any(60);
        bag.Add(5892, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Sobling, "31147-03", me).Ok);
        Assert.NotNull(j.Find(Q));
    }
}