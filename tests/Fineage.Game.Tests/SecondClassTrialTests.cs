using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// Block H2: every hand-written 2nd-profession trial can be finished on the shipped datapack. A greedy walker
/// (all rolls succeed) starts the quest, then repeatedly talks to each quest NPC, fires each quest event and kills
/// each quest mob until the mark is handed out — any dead end in the step table fails the test.
/// </summary>
public sealed class SecondClassTrialTests
{
    static readonly GameData Data = GameData.LoadDefault();

    internal static bool Walk(int questId, QuestActor me, int rewardItem, int rounds = 400)
    {
        var quest = Data.Quests.Find(questId)!;
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var bag = new PlayerInventory(Data.Items);
        var j = new QuestJournal();
        var talks = quest.Dialogs.Where(d => d.Hook == QuestHook.Talk).Select(d => d.NpcId).Distinct().ToArray();
        var events = quest.Dialogs.Where(d => d.Hook == QuestHook.Event).Select(d => (d.NpcId, d.Event)).Distinct().ToArray();
        var kills = quest.Dialogs.Where(d => d.Hook == QuestHook.Kill).Select(d => d.NpcId).Distinct().ToArray();
        for (int round = 0; round < rounds; round++)
        {
            foreach (var (npc, ev) in events)
            {
                m.AdvEvent(j, bag, questId, npc, ev, me);
                if (bag.CountOf(rewardItem) > 0)
                    return true;
            }

            foreach (int npc in talks)
            {
                m.Talk(j, bag, questId, npc, me);
                if (bag.CountOf(rewardItem) > 0)
                    return true;
            }

            foreach (int npc in kills)
                m.OnKill(j, bag, npc, me);
        }

        return false;
    }

    [Theory]
    [InlineData(211, 0, 1, 2627)]   // Trial of the Challenger — Warrior
    [InlineData(217, 0, 1, 2734)]   // Testimony of Trust — human 1st class
    [InlineData(222, 0, 1, 2762)]   // Test of the Duelist
    [InlineData(223, 3, 45, 3276)]  // Test of the Champion — Orc Raider
    public void Trial_can_be_finished(int questId, int race, int classId, int mark) =>
        Assert.True(Walk(questId, new QuestActor(race, 45, classId), mark), $"quest {questId} never handed out {mark}");

    [Theory]
    [InlineData(211, 0, 4)]   // Knight may not take the Challenger trial
    [InlineData(217, 1, 19)]  // Testimony of Trust is human only
    [InlineData(222, 0, 7)]
    [InlineData(223, 0, 4)]
    public void Trial_refuses_other_classes(int questId, int race, int classId) =>
        Assert.False(Walk(questId, new QuestActor(race, 45, classId), 57, rounds: 1) ||
                     WalkStarts(questId, new QuestActor(race, 45, classId)));

    static bool WalkStarts(int questId, QuestActor me)
    {
        var quest = Data.Quests.Find(questId)!;
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var bag = new PlayerInventory(Data.Items);
        var j = new QuestJournal();
        foreach (var d in quest.Dialogs.Where(d => d.Hook == QuestHook.Event))
            m.AdvEvent(j, bag, questId, d.NpcId, d.Event, me);
        return j.Find(questId) != null;
    }
}
