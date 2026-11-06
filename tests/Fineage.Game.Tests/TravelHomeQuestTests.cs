using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q45–49 Galladucci travel-home chain (Mark of Traveler → SoE).</summary>
public sealed class TravelHomeQuestTests
{
    const int Galladucci = 30097;
    const int Gentler = 30094;
    const int Sandra = 30090;
    const int Dustin = 30116;
    const int Mark = 7570;
    const int Order1 = 7563;
    const int Hilt = 7568;
    const int Order2 = 7564;
    const int Powder = 7567;
    const int Order3 = 7565;
    const int Neck = 7566;

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => 0.0), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor L3() => new(0, 3, 0);

    [Theory]
    [InlineData(45, 7554)]
    [InlineData(46, 7555)]
    [InlineData(47, 7556)]
    [InlineData(48, 7557)]
    [InlineData(49, 7558)]
    public void Galladucci_travel_home_runs_end_to_end(int questId, int soe)
    {
        var (m, bag, j) = Setup();
        var me = L3();
        bag.Add(Mark, 1);

        Assert.Contains($"quest_talk:{questId}", m.QuestChoiceHtml(j, Galladucci, "Galladucci", me));
        Assert.True(m.AdvEvent(j, bag, questId, Galladucci, "30097-04", me).Ok);
        Assert.Equal(1, j.Find(questId)!.Cond);
        Assert.Equal(1, bag.CountOf(Order1));

        Assert.True(m.AdvEvent(j, bag, questId, Gentler, "30094-02", me).Ok);
        Assert.Equal(2, j.Find(questId)!.Cond);
        Assert.Equal(1, bag.CountOf(Hilt));

        Assert.True(m.AdvEvent(j, bag, questId, Galladucci, "30097-07", me).Ok);
        Assert.Equal(3, j.Find(questId)!.Cond);

        Assert.True(m.AdvEvent(j, bag, questId, Sandra, "30090-02", me).Ok);
        Assert.Equal(4, j.Find(questId)!.Cond);

        Assert.True(m.AdvEvent(j, bag, questId, Galladucci, "30097-11", me).Ok);
        Assert.Equal(5, j.Find(questId)!.Cond);

        Assert.True(m.AdvEvent(j, bag, questId, Dustin, "30116-02", me).Ok);
        Assert.Equal(6, j.Find(questId)!.Cond);

        Assert.True(m.AdvEvent(j, bag, questId, Galladucci, "30097-15", me).Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(questId)!.Phase);
        Assert.Equal(1, bag.CountOf(soe));
        Assert.Equal(0, bag.CountOf(Neck));
        Assert.Equal(1, bag.CountOf(Mark)); // prior-quest mark kept
    }

    [Fact]
    public void Without_mark_accept_event_is_refused()
    {
        var (m, bag, j) = Setup();
        var me = L3();
        Assert.False(m.AdvEvent(j, bag, 45, Galladucci, "30097-04", me).Ok);
        Assert.Null(j.Find(45));
    }
}
