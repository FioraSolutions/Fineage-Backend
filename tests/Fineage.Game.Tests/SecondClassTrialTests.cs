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
    [InlineData(213, 0, 7, 2673)]   // Trial of the Seeker — Rogue
    [InlineData(224, 1, 22, 3293)]  // Test of Sagittarius — Elven Scout
    [InlineData(225, 4, 54, 2809)]  // Test of the Searcher — Scavenger
    [InlineData(219, 2, 32, 3172)]  // Testimony of Fate — Palus Knight
    [InlineData(215, 3, 49, 2721)]  // Trial of the Pilgrim — Orc Shaman
    [InlineData(227, 2, 42, 2821)]  // Test of the Reformer — Shillien Oracle
    [InlineData(228, 1, 26, 2840)]  // Test of Magus — Elven Wizard
    [InlineData(229, 2, 32, 3307)]  // Test of Witchcraft — Palus Knight
    [InlineData(229, 0, 11, 3307)]  // Test of Witchcraft — Wizard
    [InlineData(214, 2, 39, 2674)]  // Trial of the Scholar — Dark Wizard
    [InlineData(230, 1, 26, 3336)]  // Test of the Summoner — Elven Wizard
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
    [InlineData(213, 0, 1)]
    [InlineData(224, 4, 54)]  // Scavengers take the Searcher test, not Sagittarius
    [InlineData(225, 0, 4)]
    [InlineData(219, 1, 19)]  // Testimony of Fate is dark elf only
    [InlineData(215, 0, 1)]
    [InlineData(227, 1, 29)]  // Elven Oracle may not take the Reformer test
    [InlineData(228, 0, 15)]
    [InlineData(229, 1, 26)]
    [InlineData(214, 0, 15)]
    [InlineData(230, 3, 49)]
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

    [Theory]
    [InlineData(120, 0)]
    [InlineData(130, 0.5)]
    [InlineData(139, 0.95)]
    public void Kadesh_answers_lizard_blood_by_count(int blood, double expected)
    {
        // Rows are an atLeast/below ladder: exactly one chance row matches a given vial count.
        var quest = Data.Quests.Find(224)!;
        var bag = new PlayerInventory(Data.Items);
        bag.AddPieces(3306, blood);
        var rows = quest.Dialogs.Where(d => d.Hook == QuestHook.Kill && d.NpcId == 20577 && d.Chance != null &&
                                            (d.AtLeast?.All(p => bag.CountOf(p.Key) >= p.Value) ?? true) &&
                                            (d.Below?.All(p => bag.CountOf(p.Key) < p.Value) ?? true)).ToArray();
        Assert.Equal(expected == 0 ? 0 : 1, rows.Length);
        if (rows.Length == 1)
            Assert.Equal(expected, rows[0].Chance!.Value, 3);
    }

    [Fact]
    public void Below_gate_stops_a_dialog_at_the_count()
    {
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var bag = new PlayerInventory(Data.Items);
        var j = new QuestJournal();
        var rogue = new QuestActor(0, 45, 7);
        // Searcher map pieces: three torn pieces turn into Solt's map on the next lucky kill, never a fourth piece.
        m.AdvEvent(j, bag, 225, 30690, "30690-05", rogue);
        j.Find(225)!.Set("cond", "14");
        bag.AddPieces(2801, 3);
        m.OnKill(j, bag, 20551, rogue);
        Assert.Equal(0, bag.CountOf(2801));
        Assert.Equal(1, bag.CountOf(2803));
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
