using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Stub pack #809 — Q21, Q23, Q24, Q120, Q334, Q335, Q336, Q501, Q503, Q620.</summary>
public sealed class QuestStubPack809Tests
{
    static readonly GameData Data = GameData.LoadDefault();

    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
        => (new QuestMachine(Data.Quests, Data.Items, () => roll), new PlayerInventory(Data.Items), new QuestJournal());

    static QuestActor Any(int level) => new(0, level, 0);

    static QuestDefinition Def(int id) => Data.Quests.Find(id)!;

    [Theory]
    [InlineData(21)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(120)]
    [InlineData(334)]
    [InlineData(335)]
    [InlineData(336)]
    [InlineData(501)]
    [InlineData(503)]
    [InlineData(620)]
    public void Pack_starts(int questId)
    {
        var start = Def(questId).Dialogs.FirstOrDefault(d =>
            d.Ops.Any(o => o is CreateOp) && d.Ops.Any(o => o is StartOp));
        Assert.NotNull(start);
    }

    [Fact]
    public void Q21_hidden_truth_summons_duke()
    {
        var (m, bag, j) = Setup();
        const int Q = 21;
        var me = Any(65);
        Assert.True(m.AdvEvent(j, bag, Q, 31522, "31522-02", me).Ok);
        var tomb = m.AdvEvent(j, bag, Q, 31523, "31523-03", me);
        Assert.True(tomb.Ok, tomb.Message + " " + tomb.Failure);
        Assert.Contains(tomb.Extras.Summons, s => s.NpcId == 31524);
        Assert.NotNull(Def(Q).Dialogs.FirstOrDefault(d => d.Ops.Any(o => o is SummonOp)));
    }

    [Fact]
    public void Q21_finishes_at_innocentin()
    {
        var (m, bag, j) = Setup();
        const int Q = 21;
        var me = Any(65);
        m.AdvEvent(j, bag, Q, 31522, "31522-02", me);
        bag.Add(7140, 1);
        j.Find(Q)!.Set("cond", "7");
        var done = m.AdvEvent(j, bag, Q, 31328, "31328-05", me);
        Assert.True(done.Ok);
        Assert.Equal(131228, done.Exp);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q23_lidia_heart_pays_adena()
    {
        var (m, bag, j) = Setup();
        const int Q = 23;
        var me = Any(65);
        Assert.True(m.AdvEvent(j, bag, Q, 31328, "31328-02", me).Ok);
        bag.Add(7150, 1);
        j.Find(Q)!.Set("cond", "10");
        long adenaBefore = bag.CountOf(57);
        Assert.True(m.AdvEvent(j, bag, Q, 31386, "31386-03", me).Ok);
        Assert.Equal(adenaBefore + 100_000, bag.CountOf(57));
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q24_finish_event_awards_totem()
    {
        Assert.Contains(Def(24).Dialogs,
            d => d.Event == "31522-21" && d.Ops.OfType<GiveItemOp>().Any(g => g.ItemId == 7156));
        var (m, bag, j) = Setup();
        Assert.True(m.AdvEvent(j, bag, 24, 31389, "31389-03", Any(70)).Ok);
    }

    [Fact]
    public void Q336_membership_exchange_row()
    {
        Assert.Contains(Def(336).Dialogs, d => d.Event == "30232-07");
        var (m, bag, j) = Setup();
        Assert.True(m.AdvEvent(j, bag, 336, 30232, "30232-05", Any(45)).Ok);
    }

    [Fact]
    public void Q620_starts_and_brooch_exchange()
    {
        var (m, bag, j) = Setup();
        const int Q = 620, Spirit = 31453;
        var me = Any(75);
        Assert.True(m.AdvEvent(j, bag, Q, Spirit, "31453-13", me).Ok);
        foreach (int g in new[] { 7256, 7257, 7258, 7259 })
            bag.Add(g, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Spirit, "31453-16", me).Ok);
        Assert.True(bag.CountOf(7262) >= 1);
    }

    [Fact]
    public void Q503_starts_and_completes_fast_path()
    {
        var (m, bag, j) = Setup();
        const int Q = 503, Sir = 30760;
        var me = Any(40);
        Assert.True(m.AdvEvent(j, bag, Q, Sir, "30760-08", me).Ok);
        Assert.True(m.AdvEvent(j, bag, Q, Sir, "30760-23", me).Ok);
        Assert.Equal(QuestPhase.Completed, j.Find(Q)!.Phase);
    }

    [Fact]
    public void Q501_proof_finish_event()
    {
        Assert.Contains(Def(501).Dialogs,
            d => d.Event == "30756-09" && d.Ops.Any(o => o is CompleteOp));
        var (m, bag, j) = Setup();
        Assert.True(m.AdvEvent(j, bag, 501, 30756, "30756-07", Any(40)).Ok);
    }

    [Fact]
    public void Q335_laurel_turn_in()
    {
        var (m, bag, j) = Setup();
        const int Q = 335, Grey = 30744;
        var me = Any(45);
        Assert.True(m.AdvEvent(j, bag, Q, Grey, "30744_03", me).Ok);
        bag.Add(3696, 20);
        Assert.True(m.AdvEvent(j, bag, Q, Grey, "30744_16", me).Ok);
        Assert.Null(j.Find(Q));
    }

    [Fact]
    public void Q120_finish_event_gives_earring()
    {
        Assert.Contains(Def(120).Dialogs,
            d => d.Event == "32041-32" && d.Ops.OfType<GiveItemOp>().Any(g => g.ItemId == 854));
        var (m, bag, j) = Setup();
        Assert.True(m.AdvEvent(j, bag, 120, 32046, "32046-06", Any(55)).Ok);
    }

    [Fact]
    public void Q334_wishing_potion_brew()
    {
        var (m, bag, j) = Setup();
        const int Q = 334, Matild = 30738;
        var me = Any(35);
        Assert.True(m.AdvEvent(j, bag, Q, Matild, "30738-03", me).Ok);
        bag.Add(3679, 1);
        m.AdvEvent(j, bag, Q, Matild, "30738-06", me);
        foreach (int p in new[] { 3684, 3685, 3686, 3687, 3688, 3689, 3690, 3691 })
            bag.Add(p, 1);
        Assert.True(m.AdvEvent(j, bag, Q, Matild, "30738-10", me).Ok);
        Assert.True(bag.CountOf(3467) >= 1);
    }
}