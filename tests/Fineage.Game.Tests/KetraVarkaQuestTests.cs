using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Ketra Orc / Varka Silenos alliance quests 605–616 and the alliance rules (Mobius VarkaKetra behaviour).</summary>
public sealed class KetraVarkaQuestTests
{
    static readonly GameData Data = GameData.LoadDefault();
    static readonly QuestActor Me = new(0, 76, 88);

    /// <summary>Greedy walker (every roll succeeds) that also takes party drops; quit events are never pressed.</summary>
    static bool Walk(int questId, int rewardItem, PlayerInventory bag, QuestJournal j, int rounds = 600)
    {
        var quest = Data.Quests.Find(questId)!;
        var m = new QuestMachine(Data.Quests, Data.Items, () => 0.0);
        string[] quit = ["31371-26", "31378-26", "31370-09", "31377-09"];
        var talks = quest.Dialogs.Where(d => d.Hook == QuestHook.Talk).Select(d => d.NpcId).Distinct().ToArray();
        var events = quest.Dialogs.Where(d => d.Hook == QuestHook.Event && !quit.Contains(d.Event))
            .Select(d => (d.NpcId, d.Event)).Distinct().ToArray();
        var kills = quest.Dialogs.Where(d => d.Hook == QuestHook.Kill).Select(d => d.NpcId).Distinct().ToArray();
        for (int round = 0; round < rounds; round++)
        {
            foreach (var (npc, ev) in events)
            {
                m.AdvEvent(j, bag, questId, npc, ev, Me);
                if (bag.CountOf(rewardItem) > 0)
                    return true;
            }

            foreach (int npc in talks)
            {
                m.Talk(j, bag, questId, npc, Me);
                if (bag.CountOf(rewardItem) > 0)
                    return true;
            }

            foreach (int npc in kills)
            {
                m.OnKill(j, bag, npc, Me);
                m.OnPartyKill(j, bag, questId, npc, Me);
            }
        }

        return false;
    }

    static PlayerInventory Bag(params (int ItemId, int Count)[] seed)
    {
        var bag = new PlayerInventory(Data.Items);
        foreach (var (id, n) in seed)
            bag.AddPieces(id, n);
        return bag;
    }

    [Theory]
    [InlineData(605, 7215, 7219, 7220)]
    [InlineData(611, 7225, 7229, 7230)]
    public void Alliance_climbs_to_level_five(int questId, int topMark, int valor, int wisdom)
    {
        var bag = Bag((valor, 1), (wisdom, 1));
        Assert.True(Walk(questId, topMark, bag, new QuestJournal()));
        Assert.Equal(1, bag.CountOf(topMark));
    }

    [Fact]
    public void Alliance_with_the_other_side_is_refused()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        m.AdvEvent(j, Bag((7221, 1)), 605, 31371, "31371-04", Me);
        Assert.NotEqual(QuestPhase.Started, j.Find(605)?.Phase);
    }

    [Fact]
    public void Rejoining_resumes_at_the_held_mark()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        m.AdvEvent(j, Bag((7213, 1)), 605, 31371, "31371-04", Me);
        Assert.Equal(4, j.Find(605)!.Cond);
    }

    [Theory]
    [InlineData(606, 7186, 7233)]
    [InlineData(612, 7187, 7234)]
    public void War_quest_trades_100_trophies_for_20_rewards(int questId, int reward, int trophy)
    {
        var bag = Bag();
        Assert.True(Walk(questId, reward, bag, new QuestJournal()));
        Assert.Equal(20, bag.CountOf(reward));
        Assert.True(bag.CountOf(trophy) < 100);
    }

    [Theory]
    [InlineData(607, 7213, 7219)]
    [InlineData(608, 7214, 7220)]
    [InlineData(613, 7223, 7229)]
    [InlineData(614, 7224, 7230)]
    public void Raid_head_quests_pay_the_totem(int questId, int mark, int reward) =>
        Assert.True(Walk(questId, reward, Bag((mark, 1)), new QuestJournal()));

    [Theory]
    [InlineData(607, 7212)]
    [InlineData(614, 7223)]
    public void Raid_head_quests_need_the_alliance_level(int questId, int lowerMark) =>
        Assert.False(Walk(questId, questId == 607 ? 7219 : 7230, Bag((lowerMark, 1)), new QuestJournal(), 20));

    [Theory]
    [InlineData(609, 7211, 7238)]
    [InlineData(615, 7221, 7243)]
    public void Stolen_totem_part_one(int questId, int mark, int totem) =>
        Assert.True(Walk(questId, totem, Bag((mark, 1), (1661, 1)), new QuestJournal()));

    [Fact]
    public void Watcher_gaze_makes_the_box_burn_the_key()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        var bag = Bag((7211, 1), (1661, 2));
        j.Add(609, QuestPhase.Started).Set(QuestProgress.CondVar, "2");
        m.OnAttack(j, bag, 21350, Me);
        m.AdvEvent(j, bag, 609, 31561, "open_box", Me);
        Assert.Equal(1, bag.CountOf(1661));
        Assert.Equal(0, bag.CountOf(7237));
        // The shaman lifts the gaze; then the box opens.
        m.Talk(j, bag, 609, 31372, Me);
        m.AdvEvent(j, bag, 609, 31561, "open_box", Me);
        Assert.Equal(1, bag.CountOf(7237));
        Assert.Equal(3, j.Find(609)!.Cond);
    }

    [Fact]
    public void Altar_summons_ashutar_and_party_gets_the_heart()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        var bag = Bag((7238, 1));
        m.AdvEvent(j, bag, 610, 31372, "31372-02", Me);
        var summon = m.Talk(j, bag, 610, 31560, Me);
        Assert.Contains(summon.SpawnsAt, p => p.NpcId == 25316);
        Assert.Equal(2, j.Find(610)!.Cond);

        var mateJournal = new QuestJournal();
        var mateBag = Bag((7238, 1));
        m.AdvEvent(mateJournal, mateBag, 610, 31372, "31372-02", Me);
        m.OnKill(j, bag, 25316, Me);
        m.OnEveryoneKill(mateJournal, mateBag, 25316, Me);
        Assert.Equal(1, bag.CountOf(7239));
        Assert.Equal(1, mateBag.CountOf(7239));
        Assert.Equal(0, mateBag.CountOf(7238));
        m.AdvEvent(j, bag, 610, 31372, "give_heart", Me);
        Assert.Null(j.Find(610));
    }

    [Fact]
    public void Killing_an_ally_drops_the_mark_and_the_sides_quests()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var j = new QuestJournal();
        var bag = Bag((7213, 1), (7233, 5));
        j.Add(605, QuestPhase.Started).Set(QuestProgress.CondVar, "4");
        j.Add(606, QuestPhase.Started).Set(QuestProgress.CondVar, "1");
        Assert.True(QuestMachine.IsAlliedWith(21324, bag));
        Assert.False(QuestMachine.IsAlliedWith(21350, bag));

        m.BreakAlliance(j, bag, 21324);
        Assert.Equal(0, bag.CountOf(7213));
        Assert.Equal(1, bag.CountOf(7212));
        Assert.Null(j.Find(605));
        Assert.Null(j.Find(606));
        Assert.Equal(0, bag.CountOf(7233));

        m.BreakAlliance(j, bag, 21324);
        m.BreakAlliance(j, bag, 21324);
        Assert.False(QuestMachine.IsAlliedWith(21324, bag));
        Assert.Empty(m.BreakAlliance(j, bag, 21324));
    }

    [Fact]
    public void Enemy_kill_does_not_touch_the_alliance()
    {
        var m = new QuestMachine(Data.Quests, Data.Items);
        var bag = Bag((7213, 1));
        Assert.Empty(m.BreakAlliance(new QuestJournal(), bag, 21350));
        Assert.Equal(1, bag.CountOf(7213));
    }
}
