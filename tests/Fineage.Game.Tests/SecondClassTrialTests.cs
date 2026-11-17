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

            // Trials that need a quest weapon in hand (Old Knight Sword, Talin's spear) get it wielded.
            foreach (var item in bag.Items.ToArray())
                if (!item.IsEquipped && quest.Dialogs.Any(d => d.Weapons?.Contains(item.ItemId) == true))
                    bag.Equip(item);

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
    [InlineData(212, 0, 4, 2633)]   // Trial of Duty — Knight
    [InlineData(226, 0, 15, 2820)]  // Test of the Healer — Cleric
    [InlineData(218, 1, 19, 3140)]  // Testimony of Life — Elven Knight
    public void Trial_can_be_finished(int questId, int race, int classId, int mark) =>
        Assert.True(Walk(questId, new QuestActor(race, 45, classId), mark), $"quest {questId} never handed out {mark}");

    [Theory]
    [InlineData(211, 0, 4)]   // Knight may not take the Challenger trial
    [InlineData(217, 1, 19)]  // Testimony of Trust is human only
    [InlineData(222, 0, 7)]
    [InlineData(223, 0, 4)]
    [InlineData(212, 0, 1)]   // Trial of Duty is for knights
    [InlineData(226, 0, 7)]
    [InlineData(218, 0, 4)]   // Testimony of Life is elf only
    public void Trial_refuses_other_classes(int questId, int race, int classId) =>
        Assert.False(Walk(questId, new QuestActor(race, 45, classId), 57, rounds: 1) ||
                     WalkStarts(questId, new QuestActor(race, 45, classId)));

    [Fact]
    public void Healer_trial_raises_Tatoma_at_his_fixed_point()
    {
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var bag = new PlayerInventory(Data.Items);
        var j = new QuestJournal();
        var cleric = new QuestActor(0, 45, 15);
        m.AdvEvent(j, bag, 226, 30473, "30473-04", cleric);
        var outcome = m.AdvEvent(j, bag, 226, 30428, "30428-02", cleric);
        Assert.Contains(new QuestSpawnPoint(27134, -93254, 147559, -2679), outcome.SpawnsAt);
        Assert.Empty(outcome.Spawns);
        Assert.Equal(2, j.Find(226)!.Cond);
    }

    [Fact]
    public void Talin_spear_is_an_equippable_weapon()
    {
        Assert.True(Data.Items.TryGet(3026, out var spear));
        Assert.Equal(ItemKind.Weapon, spear!.Kind);
    }

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
