using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class AudienceSnapshotTests
{
    [Fact]
    public void Identical_live_audiences_share_an_id_and_release_drops_it()
    {
        AudienceSnapshots.Exclusive(() =>
        {
            int before = AudienceSnapshots.LiveCount;
            var first = AudienceSnapshots.Adopt([4, 5, 6]);
            var second = AudienceSnapshots.Adopt([4, 5, 6]);
            Assert.Equal(first, second);
            Assert.Equal(before + 1, AudienceSnapshots.LiveCount);
            Assert.Equal([4, 5, 6], AudienceSnapshots.Watchers(first));

            AudienceSnapshots.Release(first);
            AudienceSnapshots.Release(second);
            Assert.Equal(before, AudienceSnapshots.LiveCount);
        });
    }

    [Fact]
    public void Releasing_every_snapshot_reuses_slots_instead_of_keeping_history()
    {
        AudienceSnapshots.Exclusive(() =>
        {
            int beforeLive = AudienceSnapshots.LiveCount;
            int beforeHigh = AudienceSnapshots.HighWater;
            for (int i = 0; i < 1000; i++)
            {
                var id = AudienceSnapshots.Adopt([i, i + 1]);
                AudienceSnapshots.Release(id);
            }

            Assert.Equal(beforeLive, AudienceSnapshots.LiveCount);
            Assert.True(AudienceSnapshots.HighWater <= beforeHigh + 1,
                $"high water grew from {beforeHigh} to {AudienceSnapshots.HighWater}");
        });
    }

    [Fact]
    public void Knownlist_change_releases_the_cached_audience()
    {
        AudienceSnapshots.Exclusive(() =>
        {
            int before = AudienceSnapshots.LiveCount;
            var entity = new ZoneEntity { ObjectId = 11, Kind = ZoneEntityKind.Player };
            var id = AudienceSnapshots.Adopt([11, 12]);
            entity.ReplaceNetWatcherAudience(id, self: true);
            Assert.Equal(before + 1, AudienceSnapshots.LiveCount);

            entity.Knownlist.Add(13);
            Assert.Equal(before, AudienceSnapshots.LiveCount);
            Assert.True(entity.NetWatcherAudience.IsEmpty);
        });
    }

    [Fact]
    public void Clearing_an_empty_knownlist_keeps_the_cached_audience()
    {
        AudienceSnapshots.Exclusive(() =>
        {
            int before = AudienceSnapshots.LiveCount;
            var entity = new ZoneEntity { ObjectId = 15, Kind = ZoneEntityKind.Player };
            var id = AudienceSnapshots.Adopt([15]);
            entity.ReplaceNetWatcherAudience(id, self: true);

            entity.Knownlist.Clear();
            Assert.Equal(before + 1, AudienceSnapshots.LiveCount);
            entity.DropNetWatcherAudience();
            Assert.Equal(before, AudienceSnapshots.LiveCount);
        });
    }
}
