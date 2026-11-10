using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q999 T0 Tutorial — Cedric hall human fighter path (no Q255 dependency).</summary>
public sealed class T0TutorialTests
{
    const int Q = 999;
    const int Helper = 30009;
    const int Roien = 30008;
    const int Gem = 6353;
    const int Rec = 1067;
    const int Gremlin = 18342;

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => 0.0), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Fighter(int level) => new(0, level, 0);

    [Fact]
    public void Human_fighter_cedric_path_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        var me = Fighter(1);

        Assert.Contains("quest_talk:999", m.QuestChoiceHtml(j, Helper, "Newbie Helper", me));
        Assert.True(m.Talk(j, bag, Q, Helper, me).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);

        m.OnKill(j, bag, Gremlin, me);
        Assert.Equal(1, bag.CountOf(Gem));

        Assert.True(m.Talk(j, bag, Q, Helper, me).Ok);
        Assert.Equal(0, bag.CountOf(Gem));
        Assert.Equal(1, bag.CountOf(Rec));
        Assert.Equal(200, bag.CountOf(5789));
        Assert.Equal(2, j.Find(Q)!.Cond);

        Assert.True(m.AdvEvent(j, bag, Q, Roien, "30008_02", me).Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
        Assert.Equal(0, bag.CountOf(Rec));
    }
}
