using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Q419 Get a Pet and Q421 Little Wing's Big Adventure on the shipped datapack (rolls forced to 0).</summary>
public sealed class PetQuestTests
{
    const int Martin = 30731, Bella = 30256, Metty = 30072, Ellie = 30091;
    const int Cronos = 30610, Mimyu = 30747;

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup()
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => 0.0), new PlayerInventory(data.Items), new QuestJournal());
    }

    static void ReachQuiz(QuestMachine m, PlayerInventory bag, QuestJournal j, QuestActor me)
    {
        Assert.Contains("30731-03", m.Talk(j, bag, 419, Martin, me).Html);
        Assert.True(m.AdvEvent(j, bag, 419, Martin, "task", me).Ok);
        Assert.Equal(1, bag.CountOf(3418 + me.Race));
        int proof = 3423 + me.Race;
        int mob = me.Race switch { 0 => 20103, 1 => 20460, 2 => 20025, 3 => 20474, _ => 20403 };
        for (int i = 0; i < 55; i++)
            m.OnKill(j, bag, mob, me);
        Assert.Equal(50, bag.CountOf(proof));
        Assert.Contains("30731-12", m.Talk(j, bag, 419, Martin, me).Html);
        Assert.True(m.AdvEvent(j, bag, 419, Martin, "30731-12", me).Ok);
        Assert.Equal(0, bag.CountOf(proof));
        Assert.Equal(1, bag.CountOf(3417));
        Assert.True(m.AdvEvent(j, bag, 419, Bella, "30256-03", me).Ok);
        Assert.True(m.AdvEvent(j, bag, 419, Metty, "30072-02", me).Ok);
        Assert.True(m.AdvEvent(j, bag, 419, Ellie, "30091-02", me).Ok);
        Assert.Contains("quest_event:419:test", m.Talk(j, bag, 419, Martin, me).Html);
    }

    [Fact]
    public void Get_a_pet_hands_out_the_wolf_collar_after_ten_right_answers()
    {
        var (m, bag, j) = Setup();
        var me = new QuestActor(0, 15, 0);
        Assert.Contains("quest_event:419:30731-03", m.Talk(j, bag, 419, Martin, me).Html);
        ReachQuiz(m, bag, j, me);
        var page = m.AdvEvent(j, bag, 419, Martin, "test", me);
        var seen = new HashSet<string> { page.Html };
        Assert.Contains("quest_event:419:right", page.Html);
        for (int i = 0; i < 9; i++)
        {
            page = m.AdvEvent(j, bag, 419, Martin, "right", me);
            Assert.Contains("quest_event:419:right", page.Html);
            seen.Add(page.Html);
        }

        Assert.Equal(10, seen.Count); // ten different questions
        var done = m.AdvEvent(j, bag, 419, Martin, "right", me);
        Assert.True(done.Ok);
        Assert.Equal(1, bag.CountOf(2375));
        Assert.Equal(0, bag.CountOf(3417));
        Assert.Null(j.Find(419)); // repeatable: the row goes away
    }

    [Fact]
    public void Get_a_pet_wrong_answer_sends_back_to_the_visits()
    {
        var (m, bag, j) = Setup();
        var me = new QuestActor(1, 20, 18);
        ReachQuiz(m, bag, j, me);
        m.AdvEvent(j, bag, 419, Martin, "test", me);
        m.AdvEvent(j, bag, 419, Martin, "wrong", me);
        for (int i = 0; i < 8; i++)
            m.AdvEvent(j, bag, 419, Martin, "right", me);
        var fail = m.AdvEvent(j, bag, 419, Martin, "right", me);
        Assert.DoesNotContain("quest_event", fail.Html);
        Assert.Equal(0, bag.CountOf(2375));
        Assert.Equal(1, bag.CountOf(3417));
        Assert.Null(j.Find(419)!.Get("bella"));
        Assert.DoesNotContain("quest_event:419:test", m.Talk(j, bag, 419, Martin, me).Html);
        Assert.False(m.AdvEvent(j, bag, 419, Martin, "right", me) is { Ok: true, Html.Length: > 0 } r && r.Html.Contains("right"));
    }

    [Fact]
    public void Little_wing_needs_one_level_55_flute()
    {
        var (m, bag, j) = Setup();
        var me = new QuestActor(0, 50, 0);
        string none = m.Talk(j, bag, 421, Cronos, me).Html;
        bag.AddPieces(3500, 1, 40);
        string low = m.Talk(j, bag, 421, Cronos, me).Html;
        Assert.NotEqual(none, low);
        Assert.DoesNotContain("30610-06", low);
        bag.AddPieces(3501, 1, 60);
        Assert.Equal(none, m.Talk(j, bag, 421, Cronos, me).Html); // two flutes = the "one only" page
        m.AdvEvent(j, bag, 421, Cronos, "30610-06", me);
        Assert.Null(j.Find(421));
    }

    [Fact]
    public void Little_wing_runs_end_to_end_and_turns_the_flute_into_a_bugle()
    {
        var (m, bag, j) = Setup();
        var flute = bag.AddPieces(3500, 1, 56)[0];
        var me = new QuestActor(0, 50, 0);
        var withPet = me with { PetControlObjectId = flute.ObjectId };
        var otherPet = me with { PetControlObjectId = flute.ObjectId + 1000 };
        Assert.Contains("quest_event:421:30610-06", m.Talk(j, bag, 421, Cronos, me).Html);
        Assert.True(m.AdvEvent(j, bag, 421, Cronos, "30610-06", me).Ok);
        Assert.Equal(flute.ObjectId.ToString(), j.Find(421)!.Get(QuestMachine.PetVar));

        string first = m.Talk(j, bag, 421, Mimyu, me).Html;
        Assert.Contains("quest_event:421:30747-02", first);
        string wrongPet = m.Talk(j, bag, 421, Mimyu, otherPet).Html;
        string rightPet = m.Talk(j, bag, 421, Mimyu, withPet).Html;
        Assert.Contains("quest_event:421:30747-05", rightPet);
        Assert.DoesNotContain("30747-05", wrongPet);
        m.AdvEvent(j, bag, 421, Mimyu, "30747-05", otherPet);
        Assert.Equal(1, j.Find(421)!.Cond);
        Assert.True(m.AdvEvent(j, bag, 421, Mimyu, "30747-05", withPet).Ok);
        Assert.Equal(2, j.Find(421)!.Cond);
        Assert.Equal(4, bag.CountOf(4325));

        // Only the registered pet's hits count; the player's own hits do nothing.
        m.OnAttack(j, bag, 27185, me);
        m.OnAttack(j, bag, 27185, otherPet with { ByPet = true });
        Assert.Equal(4, bag.CountOf(4325));
        var byPet = withPet with { ByPet = true };
        m.OnAttack(j, bag, 27185, byPet);
        m.OnAttack(j, bag, 27185, byPet); // the same tree twice takes one leaf
        Assert.Equal(3, bag.CountOf(4325));
        foreach (int tree in new[] { 27186, 27187, 27188 })
            m.OnAttack(j, bag, tree, byPet);
        Assert.Equal(0, bag.CountOf(4325));
        Assert.Equal(3, j.Find(421)!.Cond);

        var kill = m.OnKill(j, bag, 27186, me);
        Assert.Equal(20, kill.Spawns.Count(id => id == 27189));

        string noPet = m.Talk(j, bag, 421, Mimyu, me).Html;
        Assert.True(m.Talk(j, bag, 421, Mimyu, withPet).Ok); // fairy speaks
        Assert.NotEqual(noPet, m.Talk(j, bag, 421, Mimyu, withPet).Html); // "unsummon first" page
        var done = m.Talk(j, bag, 421, Mimyu, me);
        Assert.True(done.Ok);
        Assert.Equal(0, bag.CountOf(3500));
        var bugle = Assert.Single(bag.Items, i => i.ItemId == 4422);
        Assert.Equal(56, bugle.EnchantLevel);
        Assert.Null(j.Find(421));
    }
}
