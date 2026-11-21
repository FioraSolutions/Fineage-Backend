using Fineage.Game.Quests;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class QuestJournalGeometryTests
{
    [Fact]
    public void Quest_1_step_1_has_talking_island_target()
    {
        var geo = QuestJournalGeometry.LoadDefault();
        Assert.True(geo.TryTarget(1, 1, out int x, out int y, out int z));
        Assert.Equal(-84108, x);
        Assert.Equal(244604, y);
    }
}