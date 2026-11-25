using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Issue #808 batch3 — scripts/gen_quests_partial_batch3.py.</summary>
public sealed class QuestPartialBatch3Tests
{
    [Theory]
    [InlineData(384)]
    [InlineData(420)]
    [InlineData(618)]
    [InlineData(623)]
    [InlineData(625)]
    [InlineData(641)]
    [InlineData(642)]
    [InlineData(649)]
    [InlineData(650)]
    [InlineData(654)]
    [InlineData(659)]
    [InlineData(660)]
    [InlineData(661)]
    public void Pack_has_kill_hooks_for_mobius_targets(int questId)
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == questId);
        Assert.Contains(q.Dialogs, d => d.Hook == QuestHook.Kill);
    }
}