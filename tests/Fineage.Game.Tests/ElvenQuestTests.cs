using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q00160 / Q00161 — Elven Village delivery quests on the shipped datapack.</summary>
public sealed class ElvenQuestTests
{
    static readonly QuestActor Elf3 = new(1, 3);

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items), new PlayerInventory(data.Items), new QuestJournal());
    }

    [Fact]
    public void Nerupa_gates_race_then_level_like_mobius()
    {
        var (m, bag, j) = Setup();
        Assert.Contains("Elves only", m.Talk(j, bag, 160, 30370, new QuestActor(0, 10)).Html);
        Assert.Contains("level 3", m.Talk(j, bag, 160, 30370, new QuestActor(1, 2)).Html);
        Assert.Contains("quest_event:160:accept", m.Talk(j, bag, 160, 30370, Elf3).Html);
        Assert.False(m.AdvEvent(j, bag, 160, 30370, "accept", new QuestActor(1, 2)).Ok);
        Assert.Null(j.Find(160));
    }

    [Fact]
    public void Nerupas_request_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        Assert.True(m.AdvEvent(j, bag, 160, 30370, "accept", Elf3).Ok);
        Assert.Equal(1, bag.CountOf(1026));
        Assert.True(m.Talk(j, bag, 160, 30147, Elf3).Ok); // Unoren
        Assert.Equal(0, bag.CountOf(1026));
        Assert.Equal(1, bag.CountOf(1027));
        Assert.True(m.Talk(j, bag, 160, 30149, Elf3).Ok); // Creamees
        Assert.Equal(1, bag.CountOf(1028));
        Assert.True(m.Talk(j, bag, 160, 30152, Elf3).Ok); // Julia
        Assert.Equal(1, bag.CountOf(1029));
        Assert.Equal(4, j.Find(160)!.Cond);
        var done = m.Talk(j, bag, 160, 30370, Elf3);
        Assert.True(done.Ok);
        Assert.Equal(1000, done.Exp);
        Assert.Equal(5, bag.CountOf(1060));
        Assert.Equal(0, bag.CountOf(1029));
        Assert.Equal(QuestPhase.Completed, j.Find(160)!.Phase);
        Assert.Contains("already helped", m.Talk(j, bag, 160, 30370, Elf3).Html);
    }

    [Fact]
    public void Fruit_of_the_mother_tree_runs_end_to_end()
    {
        var (m, bag, j) = Setup();
        Assert.True(m.AdvEvent(j, bag, 161, 30362, "accept", Elf3).Ok);
        Assert.Equal(1, bag.CountOf(1036));
        Assert.True(m.Talk(j, bag, 161, 30371, Elf3).Ok); // Thalia
        Assert.Equal(1, bag.CountOf(1037));
        Assert.True(m.AdvEvent(j, bag, 161, 30371, "lake", Elf3).Ok);
        var done = m.Talk(j, bag, 161, 30362, Elf3);
        Assert.True(done.Ok);
        Assert.Equal(1000, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(161)!.Phase);
    }

    [Fact]
    public void Quest_list_on_npc_shows_only_gated_quests_the_actor_can_take()
    {
        var (m, _, j) = Setup();
        Assert.Contains("quest_talk:160", m.QuestChoiceHtml(j, 30370, "Nerupa", Elf3));
    }
}

public sealed class ElvenKillQuestTests
{
    static readonly QuestActor Elf = new(1, 10);

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    [Fact]
    public void Collect_spores_caps_at_ten_then_cond_2_and_pays()
    {
        var (m, bag, j) = Setup(0.1);
        Assert.True(m.AdvEvent(j, bag, 313, 30150, "30150-05", Elf).Ok);
        for (int i = 0; i < 9; i++)
            Assert.Contains(QuestSounds.ItemGet, m.OnKill(j, bag, 20509, Elf).Sounds);
        var tenth = m.OnKill(j, bag, 20509, Elf);
        Assert.Contains(QuestSounds.Middle, tenth.Sounds);
        Assert.Equal(2, j.Find(313)!.Cond);
        m.OnKill(j, bag, 20509, Elf);
        Assert.Equal(10, bag.CountOf(1118));
        Assert.True(m.Talk(j, bag, 313, 30150, Elf).Ok);
        Assert.Equal(3500, bag.CountOf(57));
        Assert.Equal(0, bag.CountOf(1118));
        Assert.Null(j.Find(313)); // repeatable: exitQuest(true)
    }

    [Fact]
    public void Failed_roll_and_not_started_drop_nothing()
    {
        var (m, bag, j) = Setup(0.9);
        m.OnKill(j, bag, 20509, Elf);
        Assert.True(m.AdvEvent(j, bag, 313, 30150, "30150-05", Elf).Ok);
        m.OnKill(j, bag, 20509, Elf);
        Assert.Equal(0, bag.CountOf(1118));
    }

    [Fact]
    public void Wrath_of_verdure_exchanges_clubs_with_bonus_at_ten()
    {
        var (m, bag, j) = Setup(0.1);
        Assert.True(m.AdvEvent(j, bag, 267, 31853, "accept", Elf).Ok);
        Assert.Contains("No clubs", m.Talk(j, bag, 267, 31853, Elf).Html);
        for (int i = 0; i < 12; i++)
            m.OnKill(j, bag, 20325, Elf);
        Assert.Equal(12, bag.CountOf(1335));
        Assert.True(m.Talk(j, bag, 267, 31853, Elf).Ok);
        Assert.Equal(12, bag.CountOf(1340));
        Assert.Equal(600, bag.CountOf(57));
        Assert.Equal(0, bag.CountOf(1335));
        Assert.NotNull(j.Find(267));
        Assert.True(m.AdvEvent(j, bag, 267, 31853, "quit", Elf).Ok);
        Assert.Null(j.Find(267));
    }
}

public sealed class QuestItemFlagTests
{
    [Fact]
    public void Mobius_quest_items_are_flagged_and_locked()
    {
        var items = GameData.LoadDefault().Items;
        var club = items.Get(1335);
        Assert.True(club.QuestItem);
        Assert.False(club.CanSell);
        Assert.False(club.CanDropTradeStore);
        Assert.False(items.Get(1060).QuestItem); // Lesser Healing Potion
        Assert.True(items.Get(1060).CanDropTradeStore);
    }
}
