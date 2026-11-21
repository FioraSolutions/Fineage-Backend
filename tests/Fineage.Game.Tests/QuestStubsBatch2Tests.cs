using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q109 / Q376 / Q504 / Q508 / Q510 / Q628 / Q634 from batch-2 generators.</summary>
public sealed class QuestStubsBatch2Tests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q109_starts_with_golden_ram_badge()
    {
        var (m, bag, j) = Setup();
        const int Q = 109, Pierce = 31553;
        bag.Add(7246, 1);
        var me = Any(70);
        Assert.True(m.AdvEvent(j, bag, Q, Pierce, "31553-01", me).Ok);
        Assert.NotNull(j.Find(Q));
    }

    [Fact]
    public void Q504_bandit_amulet_loop()
    {
        var (m, bag, j) = Setup(0.0);
        const int Q = 504, Messenger = 35437, Amulet = 4332;
        var me = Any(40);
        Assert.True(m.Talk(j, bag, Q, Messenger, me).Ok);
        bag.Add(Amulet, 30);
        Assert.True(m.Talk(j, bag, Q, Messenger, me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q510_claw_turn_in()
    {
        var (m, bag, j) = Setup();
        const int Q = 510, Valdis = 31331, Claw = 8767;
        var me = Any(30);
        Assert.True(m.AdvEvent(j, bag, Q, Valdis, "31331-3", me).Ok);
        bag.Add(Claw, 10);
        Assert.True(m.Talk(j, bag, Q, Valdis, me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q634_fragment_quest_starts()
    {
        var (m, bag, j) = Setup();
        const int Q = 634, Gate = 31494;
        var me = Any(25);
        Assert.True(m.AdvEvent(j, bag, Q, Gate, "02", me).Ok);
        Assert.NotNull(j.Find(Q));
    }

    [Fact]
    public void Q376_starts_at_sobling()
    {
        var (m, bag, j) = Setup();
        const int Q = 376, Sobling = 31147;
        var me = Any(55);
        Assert.True(m.AdvEvent(j, bag, Q, Sobling, "31147-03", me).Ok);
        Assert.NotNull(j.Find(Q));
    }
}