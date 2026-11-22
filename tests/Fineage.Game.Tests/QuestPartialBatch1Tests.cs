using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Issue #808 batch1 — Mobius kill targets via scripts/wire_mobius_kill_targets.py.</summary>
public sealed class QuestPartialBatch1Tests
{
    static (QuestMachine M, PlayerInventory Bag, QuestJournal J) Setup(double roll = 0.0)
    {
        var data = GameData.LoadDefault();
        return (new QuestMachine(data.Quests, data.Items, () => roll), new PlayerInventory(data.Items), new QuestJournal());
    }

    static QuestActor Any(int level) => new(0, level, 0);

    [Theory]
    [InlineData(25)]
    [InlineData(32)]
    [InlineData(34)]
    [InlineData(35)]
    [InlineData(36)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(44)]
    [InlineData(103)]
    [InlineData(117)]
    public void Pack_has_kill_hooks_for_mobius_targets(int questId)
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == questId);
        Assert.Contains(q.Dialogs, d => d.Hook == QuestHook.Kill);
    }

    [Fact]
    public void Q32_kill_step_drops_herb_with_party_and_cond_3()
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == 32);
        var kill = q.Dialogs.First(d => d.Hook == QuestHook.Kill && d.NpcId == 20135);
        Assert.Equal(3, kill.Cond);
        Assert.True(kill.Party);
        var drop = Assert.Single(kill.Ops.OfType<DropItemOp>());
        Assert.Equal(7166, drop.ItemId);
        Assert.Equal(20, drop.Limit);
    }

    [Fact]
    public void Q42_kill_step_caps_thirty_pieces_and_advances_cond()
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == 42);
        var kill = q.Dialogs.First(d => d.Hook == QuestHook.Kill && d.NpcId == 20068);
        Assert.Equal(2, kill.Cond);
        var drop = Assert.Single(kill.Ops.OfType<DropItemOp>());
        Assert.Equal(7548, drop.ItemId);
        Assert.Equal(30, drop.Limit);
        Assert.Equal(3, drop.CondAtLimit);
    }
}