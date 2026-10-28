using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class NpcObjectIdAllocatorTests
{
    [Fact]
    public void Town_ids_sit_between_legacy_npcs_and_items()
    {
        Assert.Equal(50_000, NpcObjectIdAllocator.LegacyFirst);
        Assert.Equal(60_000, NpcObjectIdAllocator.First);
        Assert.Equal(PlayerInventory.FirstObjectId, NpcObjectIdAllocator.ExclusiveEnd);

        var ids = new NpcObjectIdAllocator();
        ids.Reserve(50_001);
        ids.Reserve(60_000);
        Assert.Equal(60_001, ids.Next());
        Assert.Equal(60_002, ids.Next());
        Assert.Throws<InvalidOperationException>(() => ids.Reserve(60_001));
        Assert.Throws<ArgumentOutOfRangeException>(() => ids.Reserve(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ids.Reserve(PlayerInventory.FirstObjectId));
    }
}
