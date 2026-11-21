using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q127 / Q384 / Q386 / Q422 / Q648 from batch-3 generator.</summary>
public sealed class QuestStubsBatch3Tests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0) =>
        (new QuestMachine(GameData.LoadDefault().Quests, GameData.LoadDefault().Items, () => roll), new PlayerInventory(GameData.LoadDefault().Items), new QuestJournal());

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q127_starts_and_finishes_at_dominic()
    {
        var (m, bag, j) = Setup();
        const int Q = 127;
        var me = Any(20);
        Assert.True(m.AdvEvent(j, bag, Q, 31350, "31350-04", me).Ok);
        Assert.NotNull(j.Find(Q));
        Assert.True(m.AdvEvent(j, bag, Q, 30187, "30187-06", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 30187, "30187-08", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 32092, "32092-05", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 31288, "31288-04", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 30862, "30862-04", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 30113, "30113-04", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 30756, "kamaelstory", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 30756, "30756-05", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, 31350, "31350-06", me).Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q386_accept_and_turn_in()
    {
        var (m, bag, j) = Setup();
        const int Q = 386, Romp = 30843, Ore = 6363;
        var me = Any(60);
        Assert.True(m.AdvEvent(j, bag, Q, Romp, "QUEST_ACCEPTED", me).Ok);
        bag.Add(Ore, 100);
        Assert.True(m.Talk(j, bag, Q, Romp, me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q384_accept_and_turn_in()
    {
        var (m, bag, j) = Setup();
        const int Q = 384, Cliff = 30182, Medal = 5964;
        var me = Any(45);
        Assert.True(m.AdvEvent(j, bag, Q, Cliff, "QUEST_ACCEPTED", me).Ok);
        bag.Add(Medal, 10);
        Assert.True(m.Talk(j, bag, Q, Cliff, me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q422_low_level_start_and_katari()
    {
        var (m, bag, j) = Setup();
        const int Q = 422, Judge = 30981, Katari = 30668, Skull = 4326;
        var me = Any(18);
        Assert.True(m.AdvEvent(j, bag, Q, Judge, "Start", me).Ok);
        Assert.True(m.Talk(j, bag, Q, Katari, me).Ok);
        bag.Add(Skull, 10);
        Assert.True(m.Talk(j, bag, Q, Katari, me).Ok);
        Assert.Equal(10, j.Find(Q)!.Cond);
    }

    [Fact]
    public void Q648_starts_at_rafforty()
    {
        var (m, bag, j) = Setup();
        const int Q = 648, Rafforty = 32020;
        var me = Any(55);
        Assert.True(m.AdvEvent(j, bag, Q, Rafforty, "32020-04", me).Ok);
        Assert.NotNull(j.Find(Q));
    }
}