using Fineage.Zone.Simulation;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Step 7 of <c>docs/architecture/zone-simulation.md</c>: region ownership, active sets and handoff.</summary>
public sealed class RegionDirectoryTests
{
    /// <summary>1000-unit cells, handed off 100 units past the edge.</summary>
    static readonly GridRegionPartition Cells = new(1000, handoffMargin: 100);

    static RegionId Cell(int x, int y = 0, int instance = 0) => new(instance, x, y);

    [Fact]
    public void Dropping_an_emptied_region_drops_its_border_cache_and_a_recreated_region_republishes()
    {
        var directory = new RegionDirectory<Member>(Cells);
        directory.Add(1, new Member(1, 990, 10));
        var first = directory.BorderMembers(50).ToList();
        Assert.Single(first);
        Assert.Equal(1, directory.BorderCacheCount);

        Assert.True(directory.Remove(1));
        Assert.Equal(0, directory.BorderCacheCount);

        directory.Add(2, new Member(2, 980, 20));
        var again = directory.BorderMembers(50).ToList();
        Assert.Equal(2, Assert.Single(Assert.Single(again).Members).ObjectId);
    }

    [Fact]
    public void Entities_are_owned_by_the_region_of_their_position()
    {
        var directory = new RegionDirectory<Member>(Cells);
        directory.Add(1, new Member(1, 100, 100));
        directory.Add(2, new Member(2, 1500, 100));
        directory.Add(3, new Member(3, 1700, -50));

        Assert.True(directory.TryOwnerOf(1, out var first));
        Assert.Equal(new RegionPlacement(Cell(0), 0), first);
        Assert.True(directory.TryOwnerOf(2, out var second));
        Assert.Equal(Cell(1), second.Region);
        Assert.True(directory.TryOwnerOf(3, out var third));
        Assert.Equal(Cell(1, -1), third.Region);
        Assert.False(directory.TryOwnerOf(4, out _));
        Assert.Equal([Cell(0), Cell(1, -1), Cell(1)], directory.Regions.Select(r => r.Id));
        directory.Verify();
    }

    [Fact]
    public void Lookups_keep_the_order_and_semantics_of_a_dictionary()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var reference = new Dictionary<int, Member>();
        void Put(Member m)
        {
            directory[m.ObjectId] = m;
            reference[m.ObjectId] = m;
        }

        void Drop(int id) => Assert.Equal(reference.Remove(id), directory.Remove(id));

        Put(new Member(30, 0, 0));
        Put(new Member(10, 5000, 0));
        Put(new Member(20, 0, 0));
        Drop(10);
        Put(new Member(40, 2500, 0));
        Put(new Member(30, 9000, 0));
        Drop(99);

        Assert.Equal(reference.Keys, directory.Keys);
        Assert.Equal(reference.Values, directory.Values);
        Assert.Equal(3, directory.Count);
        Assert.Same(reference[30], directory[30]);
        Assert.Equal(Cell(9), directory.OwnerOf(directory[30]).Region);
        Assert.Null(directory.GetValueOrDefault(10));
        var missing = Assert.Throws<KeyNotFoundException>(() => directory[10]);
        Assert.Contains("entity 10 is not in the world", missing.Message);
        Assert.Throws<ArgumentException>(() => directory.Add(20, new Member(20, 0, 0)));
        Assert.Throws<ArgumentException>(() => directory.Add(21, new Member(22, 0, 0)));
        directory.Verify();
    }

    [Fact]
    public void Sets_joined_before_placement_are_installed_with_the_entity_and_cleared_on_removal()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var member = new Member(7, 1200, 0);
        directory.Join(member, ActiveSets.NeedRegen);
        directory.Join(member, ActiveSets.Moving);
        Assert.Equal(0, directory.CountOf(ActiveSets.NeedRegen));

        directory.Add(7, member);
        Assert.Equal(1, directory.CountOf(ActiveSets.NeedRegen));
        Assert.Equal([7], directory.Snapshot(ActiveSets.Moving));
        Assert.True(directory.Regions.Single().IsIn(7, ActiveSets.Moving));

        directory.Leave(member, ActiveSets.Moving);
        directory.Leave(member, ActiveSets.Moving);
        Assert.Equal(0, directory.CountOf(ActiveSets.Moving));
        Assert.Equal(ActiveSets.NeedRegen, member.ActiveSets);

        Assert.True(directory.Remove(7));
        Assert.Equal(ActiveSets.None, member.ActiveSets);
        Assert.Equal(0, directory.CountOf(ActiveSets.NeedRegen));
        Assert.Empty(directory.Regions);
        directory.Verify();
    }

    [Fact]
    public void A_stale_object_with_a_placed_id_does_not_touch_the_placed_one()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var placed = new Member(5, 0, 0);
        directory.Add(5, placed);
        var stale = new Member(5, 0, 0);

        directory.Join(stale, ActiveSets.Thinking);

        Assert.Equal(0, directory.CountOf(ActiveSets.Thinking));
        Assert.Equal(ActiveSets.None, placed.ActiveSets);
        var error = Assert.Throws<InvalidOperationException>(() => directory.OwnerOf(stale));
        Assert.Equal("entity 5 is not placed in any region", error.Message);
        directory.Verify();
    }

    [Fact]
    public void Snapshot_and_collect_go_region_by_region_with_ascending_ids()
    {
        var directory = new RegionDirectory<Member>(Cells);
        foreach (var (id, x) in new[] { (9, 2100), (4, 2900), (8, 100), (3, 900), (6, -10) })
        {
            var member = new Member(id, x, 0);
            directory.Add(id, member);
            directory.Join(member, ActiveSets.WithAbnormals);
        }

        Assert.Equal([6, 3, 8, 4, 9], directory.Snapshot(ActiveSets.WithAbnormals));
        Assert.Empty(directory.Snapshot(ActiveSets.Decaying));

        var work = new RegionWorkCollector();
        directory.Collect(ActiveSets.WithAbnormals, work);
        var buckets = work.Drain();
        Assert.Equal([Cell(-1), Cell(0), Cell(2)], buckets.Select(b => b.Region));
        Assert.Equal([3, 8], buckets[1].ObjectIds);
    }

    [Fact]
    public void Handoff_waits_for_the_margin_and_carries_memberships_and_a_rising_epoch()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var member = new Member(1, 900, 500);
        directory.Add(1, member);
        directory.Join(member, ActiveSets.Moving);

        member.X = 1099;
        directory.Relocated(1);
        Assert.Empty(directory.Handoff());
        Assert.Equal(new RegionPlacement(Cell(0), 0), directory.OwnerOf(member));
        directory.Verify();

        member.X = 1100;
        directory.Relocated(1);
        Assert.Equal([new RegionTransfer(1, Cell(0), Cell(1), 1)], directory.Handoff());
        Assert.Equal(new RegionPlacement(Cell(1), 1), directory.OwnerOf(member));
        Assert.True(directory.Regions.Single().IsIn(1, ActiveSets.Moving));
        Assert.Equal(1, directory.CountOf(ActiveSets.Moving));
        directory.Verify();

        member.X = 900;
        directory.Relocated(1);
        Assert.Empty(directory.Handoff());

        member.X = 899;
        directory.Relocated(1);
        Assert.Equal([new RegionTransfer(1, Cell(1), Cell(0), 2)], directory.Handoff());
        Assert.Empty(directory.Handoff());
        directory.Verify();
    }

    [Fact]
    public void Handoff_runs_in_ascending_id_order_and_skips_entities_removed_since_they_moved()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var members = new[] { new Member(30, 0, 0), new Member(10, 0, 0), new Member(20, 0, 0) };
        foreach (var m in members)
            directory.Add(m.ObjectId, m);

        foreach (var m in members)
        {
            m.X = 5000;
            directory.Relocated(m.ObjectId);
        }

        directory.Relocated(99);
        directory.Remove(20);

        Assert.Equal([10, 30], directory.Handoff().Select(t => t.ObjectId));
        directory.Verify();
    }

    [Fact]
    public void An_instance_change_hands_off_even_without_moving()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var member = new Member(1, 500, 500);
        directory.Add(1, member);

        member.InstanceId = 3;
        directory.Relocated(1);

        Assert.Equal([new RegionTransfer(1, Cell(0), Cell(0, instance: 3), 1)], directory.Handoff());
        directory.Verify();
    }

    [Fact]
    public void Verify_reports_an_entity_that_left_its_region_without_being_relocated()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var member = new Member(1, 500, 500);
        directory.Add(1, member);

        member.X = 4000;

        var error = Assert.Throws<InvalidOperationException>(directory.Verify);
        Assert.Contains("entity 1 at 0:4000,500 is outside its region 0:0_0 and was not relocated", error.Message);
        directory.Relocated(1);
        directory.Verify();
    }

    [Theory]
    [InlineData(ActiveSets.None)]
    [InlineData(ActiveSets.Moving | ActiveSets.Thinking)]
    [InlineData((ActiveSets)(1 << 12))]
    public void A_set_operation_needs_exactly_one_known_set(ActiveSets set)
    {
        var directory = new RegionDirectory<Member>(Cells);
        var member = new Member(1, 0, 0);
        directory.Add(1, member);

        Assert.Throws<ArgumentOutOfRangeException>(() => directory.Join(member, set));
        Assert.Throws<ArgumentOutOfRangeException>(() => directory.CountOf(set));
    }

    sealed class Member(int objectId, int x, int y) : IRegionMember
    {
        public int ObjectId { get; } = objectId;
        public int InstanceId { get; set; }
        public int X { get; set; } = x;
        public int Y { get; set; } = y;
        public ActiveSets ActiveSets { get; set; }
    }
}
