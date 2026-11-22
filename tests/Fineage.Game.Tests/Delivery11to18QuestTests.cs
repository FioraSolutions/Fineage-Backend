using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Mobius Q11–Q18 delivery / altar chain from scripts/gen_quests_11_18_delivery.py (#808/#810).</summary>
public sealed class Delivery11to18QuestTests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Fact]
    public void Q11_ketra_meeting_walks_to_completion()
    {
        var (m, bag, j) = Setup();
        const int Q = 11, Cadmon = 31296, Leon = 31256, Wahkan = 31371, Box = 7231;

        // Level gate: 73 gets the refusal page (not the 74+ start page) and cannot start.
        var low = m.Talk(j, bag, Q, Cadmon, Any(73));
        var high = m.Talk(j, bag, Q, Cadmon, Any(74));
        Assert.True(low.Ok && high.Ok);
        Assert.NotEqual(high.HtmlKey ?? high.Html, low.HtmlKey ?? low.Html);
        Assert.False(m.AdvEvent(j, bag, Q, Cadmon, "31296-03", Any(73)).Ok);

        Assert.True(m.AdvEvent(j, bag, Q, Cadmon, "31296-03", Any(74)).Ok);
        Assert.NotNull(j.Find(Q));

        // Cadmon has nothing new; Leon hands over the munitions box.
        Assert.True(m.Talk(j, bag, Q, Cadmon, Any(74)).Ok);
        Assert.True(m.Talk(j, bag, Q, Leon, Any(74)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Leon, "31256-02", Any(74)).Ok);
        Assert.Equal(1, bag.CountOf(Box));
        Assert.Equal(2, j.Find(Q)!.Cond);

        // Wahkan takes the box; Mobius exp 79761 and one-time completion.
        var done = m.AdvEvent(j, bag, Q, Wahkan, "31371-02", Any(74));
        Assert.True(done.Ok);
        Assert.Equal(79761, done.Exp);
        Assert.Equal(0, done.Sp);
        Assert.Equal(0, bag.CountOf(Box));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q12_varka_meeting_walks_to_completion()
    {
        var (m, bag, j) = Setup();
        const int Q = 12, Cadmon = 31296, Helmut = 31258, Ashanuk = 31378, Box = 7232;

        Assert.True(m.AdvEvent(j, bag, Q, Cadmon, "31296-03", Any(74)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Helmut, "31258-02", Any(74)).Ok);
        Assert.Equal(1, bag.CountOf(Box));
        var done = m.AdvEvent(j, bag, Q, Ashanuk, "31378-02", Any(74));
        Assert.True(done.Ok);
        Assert.Equal(79761, done.Exp);
        Assert.Equal(0, bag.CountOf(Box));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q13_parcel_delivery_pays_mobius_adena()
    {
        var (m, bag, j) = Setup();
        const int Q = 13, Fundin = 31274, Vulcan = 31539, Package = 7263;

        Assert.False(m.AdvEvent(j, bag, Q, Fundin, "31274-2", Any(73)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Fundin, "31274-2", Any(74)).Ok);
        Assert.Equal(1, bag.CountOf(Package));

        long adenaBefore = bag.CountOf(57);
        var done = m.AdvEvent(j, bag, Q, Vulcan, "31539-1", Any(74));
        Assert.True(done.Ok);
        Assert.Equal(0, bag.CountOf(Package));
        Assert.Equal(adenaBefore + 82656, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q14_archaeologist_letter_delivered()
    {
        var (m, bag, j) = Setup();
        const int Q = 14, Liesel = 31263, Ghost = 31538, Letter = 7253;

        Assert.True(m.AdvEvent(j, bag, Q, Liesel, "31263-2", Any(74)).Ok);
        Assert.Equal(1, bag.CountOf(Letter));
        long adenaBefore = bag.CountOf(57);
        Assert.True(m.AdvEvent(j, bag, Q, Ghost, "31538-1", Any(74)).Ok);
        Assert.Equal(0, bag.CountOf(Letter));
        Assert.Equal(adenaBefore + 113228, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q15_sweet_whispers_two_step_talk_chain()
    {
        var (m, bag, j) = Setup();
        const int Q = 15, Vladimir = 31302, Hierarch = 31517, Necro = 31518;

        Assert.False(m.AdvEvent(j, bag, Q, Vladimir, "31302-01", Any(59)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Vladimir, "31302-01", Any(60)).Ok);
        Assert.Equal(1, j.Find(Q)!.Cond);

        // Hierarch refuses before the necro step.
        Assert.False(m.Talk(j, bag, Q, Hierarch, Any(60)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Necro, "31518-01", Any(60)).Ok);
        Assert.Equal(2, j.Find(Q)!.Cond);

        var done = m.AdvEvent(j, bag, Q, Hierarch, "31517-01", Any(60));
        Assert.True(done.Ok);
        Assert.Equal(60217, done.Exp);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q16_five_altars_consume_crystals_then_complete()
    {
        var (m, bag, j) = Setup();
        const int Q = 16, Hierarch = 31517, Crystal = 7167;

        Assert.True(m.AdvEvent(j, bag, Q, Hierarch, "31517-2", Any(62)).Ok);
        Assert.Equal(5, bag.CountOf(Crystal));

        for (int k = 1; k <= 5; k++)
        {
            int altar = 31511 + k;
            Assert.Equal(k, j.Find(Q)!.Cond);
            var talk = m.Talk(j, bag, Q, altar, Any(62));
            Assert.True(talk.Ok);
            Assert.True(m.AdvEvent(j, bag, Q, altar, $"{altar}-1", Any(62)).Ok);
            Assert.Equal(5 - k, bag.CountOf(Crystal));
            Assert.Equal(k + 1, j.Find(Q)!.Cond);
        }

        var done = m.Talk(j, bag, Q, Hierarch, Any(62));
        Assert.True(done.Ok);
        Assert.Equal(221958, done.Exp);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q16_hierarch_quits_when_crystals_gone()
    {
        var (m, bag, j) = Setup();
        const int Q = 16, Hierarch = 31517, Crystal = 7167;

        Assert.True(m.AdvEvent(j, bag, Q, Hierarch, "31517-2", Any(62)).Ok);
        Assert.True(bag.TryTake(Crystal, 5, new List<BagTouch>()));

        // Mobius st.exitQuest(true): the started row is deleted, quest can be retaken.
        var quit = m.Talk(j, bag, Q, Hierarch, Any(62));
        Assert.True(quit.Ok);
        Assert.Null(j.Find(Q));
        Assert.True(m.AdvEvent(j, bag, Q, Hierarch, "31517-2", Any(62)).Ok);
        Assert.NotNull(j.Find(Q));
    }

    [Fact]
    public void Q17_four_altars_with_empty_hand_retry_page()
    {
        var (m, bag, j) = Setup();
        const int Q = 17, Hierarch = 31517, Blood = 7168;

        Assert.True(m.AdvEvent(j, bag, Q, Hierarch, "31517-04", Any(61)).Ok);
        Assert.Equal(4, bag.CountOf(Blood));

        for (int k = 1; k <= 4; k++)
        {
            int altar = 31507 + k;
            // Mobius: the altar event without blood shows the retry page and keeps cond.
            Assert.True(bag.TryTake(Blood, bag.CountOf(Blood), new List<BagTouch>()));
            var retry = m.AdvEvent(j, bag, Q, altar, $"{altar}-02", Any(61));
            Assert.True(retry.Ok);
            Assert.Equal(k, j.Find(Q)!.Cond);
            bag.Add(Blood, 4 - k + 1);

            Assert.True(m.AdvEvent(j, bag, Q, altar, $"{altar}-02", Any(61)).Ok);
            Assert.Equal(k + 1, j.Find(Q)!.Cond);
        }

        var done = m.Talk(j, bag, Q, Hierarch, Any(61));
        Assert.True(done.Ok);
        Assert.Equal(105527, done.Exp);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q18_golden_ram_supply_box_walks_to_completion()
    {
        var (m, bag, j) = Setup();
        const int Q = 18, Donal = 31314, Daisy = 31315, Abercrombie = 31555, Box = 7245;

        Assert.False(m.AdvEvent(j, bag, Q, Donal, "31314-03", Any(65)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Donal, "31314-03", Any(66)).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Daisy, "31315-02", Any(66)).Ok);
        Assert.Equal(1, bag.CountOf(Box));

        long adenaBefore = bag.CountOf(57);
        var done = m.AdvEvent(j, bag, Q, Abercrombie, "31555-02", Any(66));
        Assert.True(done.Ok);
        Assert.Equal(50000, done.Exp);
        Assert.Equal(0, bag.CountOf(Box));
        Assert.Equal(adenaBefore + 15000, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }
}
