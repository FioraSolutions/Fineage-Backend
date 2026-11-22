using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>Issue #808 batch4 — talk + kill gap wiring.</summary>
public sealed class QuestPartialBatch4Tests
{
    [Theory]
    [InlineData(31)]
    [InlineData(119)]
    [InlineData(299)]
    [InlineData(344)]
    public void Wired_pack_loads_in_catalog(int questId)
    {
        var q = GameData.LoadDefault().Quests.Quests.First(x => x.Id == questId);
        Assert.True(q.Dialogs.Count > 2);
    }
}