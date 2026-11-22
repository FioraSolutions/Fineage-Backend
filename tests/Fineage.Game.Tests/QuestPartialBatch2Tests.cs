using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Issue #808 batch2 — scripts/gen_quests_partial_batch2.py.</summary>
public sealed class QuestPartialBatch2Tests
{
    [Theory]
    [InlineData(105)]
    [InlineData(114)]
    [InlineData(327)]
    [InlineData(330)]
    [InlineData(337)]
    [InlineData(340)]
    [InlineData(345)]
    [InlineData(348)]
    [InlineData(351)]
    [InlineData(352)]
    [InlineData(356)]
    [InlineData(359)]
    [InlineData(370)]
    [InlineData(380)]
    [InlineData(381)]
    public void Pack_has_kill_hooks_for_mobius_targets(int questId)
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == questId);
        Assert.Contains(q.Dialogs, d => d.Hook == QuestHook.Kill);
    }
}