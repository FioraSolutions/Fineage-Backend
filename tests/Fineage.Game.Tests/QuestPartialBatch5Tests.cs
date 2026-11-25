using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Issue #808 batch5 — framework wiring (rebind, completion, spawns, teleports).</summary>
public sealed class QuestPartialBatch5Tests
{
    [Theory]
    [InlineData(111)]
    [InlineData(334)]
    [InlineData(620)]
    [InlineData(505)]
    public void Wired_pack_loads_in_catalog(int questId)
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == questId);
        Assert.True(q.Dialogs.Count > 2);
    }

    [Fact]
    public void Quest_111_has_item_chance_drops()
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == 111);
        var kills = q.Dialogs.Where(d => d.Hook == QuestHook.Kill).ToList();
        Assert.True(kills.Count >= 10);
        Assert.Contains(kills.SelectMany(d => d.Ops), o => o is DropItemOp);
    }

    [Fact]
    public void Quest_620_has_teleport_ops()
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == 620);
        Assert.Contains(q.Dialogs.SelectMany(d => d.Ops), o => o is TeleportOp);
    }
}