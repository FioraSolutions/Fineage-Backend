using Fineage.Game.Recipes;
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

    internal static bool Walk(int questId, QuestActor me, int rewardItem, int rounds = 400, bool craft = false,
        params (int ItemId, int Count)[] seed)
    {
        var quest = Data.Quests.Find(questId)!;
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var bag = new PlayerInventory(Data.Items);
        foreach (var (itemId, count) in seed)
            bag.AddPieces(itemId, count);
        var j = new QuestJournal();
        var known = new List<RecipeDefinition>();
        var talks = quest.Dialogs.Where(d => d.Hook == QuestHook.Talk).Select(d => d.NpcId).Distinct().ToArray();
        var events = quest.Dialogs.Where(d => d.Hook == QuestHook.Event).Select(d => (d.NpcId, d.Event)).Distinct().ToArray();
        var kills = quest.Dialogs.Where(d => d.Hook == QuestHook.Kill).Select(d => d.NpcId).Distinct().ToArray();
        var hits = quest.Dialogs.Where(d => d.Hook == QuestHook.Attack).Select(d => d.NpcId).Distinct().ToArray();
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
            foreach (int npc in hits)
                m.OnAttack(j, bag, npc, me);

            // Dwarven trials craft their quest items (Journeyman Ring, Key of Titan): learn each recipe scroll, then
            // make the item whenever every ingredient is in the bag.
            if (craft)
                Craft(bag, known);
        }

        return false;
    }

    static void Craft(PlayerInventory bag, List<RecipeDefinition> known)
    {
        foreach (var item in bag.Items.ToArray())
            if (Data.Recipes.TryGetByRecipeItem(item.ItemId, out var learn) && !known.Contains(learn)
                && bag.TryTake(item.ItemId, 1, new List<BagTouch>()))
                known.Add(learn);
        foreach (var r in known)
            while (r.Ingredients.All(i => bag.CountOf(i.ItemId) >= i.Count))
            {
                foreach (var i in r.Ingredients)
                    bag.TryTake(i.ItemId, i.Count, new List<BagTouch>());
                bag.AddPieces(r.Production.ItemId, r.Production.Count);
            }
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
    [InlineData(215, 3, 50, 2721)]  // Trial of the Pilgrim — Orc Shaman
    [InlineData(227, 2, 42, 2821)]  // Test of the Reformer — Shillien Oracle
    [InlineData(228, 1, 26, 2840)]  // Test of Magus — Elven Wizard
    [InlineData(229, 2, 32, 3307)]  // Test of Witchcraft — Palus Knight
    [InlineData(229, 0, 11, 3307)]  // Test of Witchcraft — Wizard
    [InlineData(214, 2, 39, 2674)]  // Trial of the Scholar — Dark Wizard
    [InlineData(230, 1, 26, 3336)]  // Test of the Summoner — Elven Wizard
    [InlineData(220, 3, 50, 3203)]  // Testimony of Glory — Orc Shaman
    [InlineData(233, 3, 50, 2879)]  // Test of the War Spirit — Orc Shaman
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
    [InlineData(220, 0, 1)]   // Testimony of Glory is orc only
    [InlineData(233, 3, 45)]
    [InlineData(216, 0, 1)]
    [InlineData(221, 0, 1)]   // Testimony of Prosperity is dwarf only
    [InlineData(231, 4, 54)]  // Maestro is for Artisans
    [InlineData(232, 3, 47)]
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

    [Fact]
    public void Lord_trial_can_be_finished_with_adena_and_bone_arrows() =>
        Assert.True(Walk(232, new QuestActor(3, 45, 50), 3390, 400, false, (57, 1000), (1341, 1000)));

    [Theory]
    [InlineData(216, 4, 54, 3119)]  // Trial of the Guildsman — Scavenger
    [InlineData(216, 4, 56, 3119)]  // Trial of the Guildsman — Artisan
    [InlineData(221, 4, 54, 3238)]  // Testimony of Prosperity — Scavenger
    [InlineData(231, 4, 56, 2867)]  // Test of the Maestro — Artisan
    public void Dwarf_trial_can_be_finished_by_crafting(int questId, int race, int classId, int mark) =>
        Assert.True(Walk(questId, new QuestActor(race, 45, classId), mark, 400, true, (57, 10000), (1458, 200),
            (1867, 100)), $"quest {questId} never handed out {mark}");

    [Fact]
    public void Fates_whisper_forges_an_A_grade_weapon() =>
        // The walker picks every B-grade in turn, so the last one (Kaim Vanul's Bones) is the confirmed choice.
        Assert.True(Walk(234, new QuestActor(0, 76, 88), 5011, 400, false, (1460, 984), (7893, 1)));

    [Fact]
    public void Fates_whisper_needs_level_75() =>
        Assert.False(Walk(234, new QuestActor(0, 74, 88), 5011, 50, false, (1460, 984), (7893, 1)));

    [Theory]
    [InlineData(0.10, 2)]
    [InlineData(0.30, 1)]
    [InlineData(0.90, 0)]
    public void Champion_harpy_hit_calls_matriarchs(double roll, int expected)
    {
        var m = new QuestMachine(Data.Quests, Data.Items, () => roll);
        var j = new QuestJournal();
        j.Add(223, QuestPhase.Started).Set(QuestProgress.CondVar, "6");
        var outcome = m.OnAttack(j, new PlayerInventory(Data.Items), 20145, new QuestActor(0, 40, 1));
        Assert.Equal(expected, outcome.Spawns.Count(id => id == 27088));
    }

    [Fact]
    public void Raid_boss_kill_leaves_the_fates_whisper_chest_for_anyone()
    {
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var outcome = m.OnKill(new QuestJournal(), new PlayerInventory(Data.Items), 25035, new QuestActor(0, 76, 88));
        Assert.Contains(31027, outcome.Spawns);
    }

    [Fact]
    public void Baium_reddens_the_pipette_knife_only_when_it_is_in_hand()
    {
        const int Knife = 4665, Red = 4673, Baium = 29020;
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        var bag = new PlayerInventory(Data.Items);
        var j = new QuestJournal();
        var me = new QuestActor(0, 76, 88);
        j.Add(234, QuestPhase.Started).Set(QuestProgress.CondVar, "7");
        bag.AddPieces(Knife, 1);
        m.OnAttack(j, bag, Baium, me);
        Assert.Equal(0, bag.CountOf(Red));
        bag.Equip(bag.FindByItemId(Knife)!);
        m.OnAttack(j, bag, Baium, me);
        Assert.Equal(1, bag.CountOf(Red));
        Assert.Equal(0, bag.CountOf(Knife));
        Assert.True(m.HasAttackSteps(Baium));
        Assert.False(m.HasAttackSteps(20001));
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
