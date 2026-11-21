using Fineage.Zone.Simulation;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Step 8 of <c>docs/architecture/zone-simulation.md</c>, Simulation layer: region bodies run in scopes committed in
/// order, the directory refuses what a body may not touch, regions index their entities by cell, and border
/// snapshots answer the neighbour half of a spatial query.
/// </summary>
public sealed class RegionBodyIsolationTests
{
    /// <summary>1000-unit regions, handed off 100 units past the edge; 250-unit spatial cells.</summary>
    static readonly GridRegionPartition Cells = new(1000, handoffMargin: 100);

    static RegionId Cell(int x, int y = 0, int instance = 0) => new(instance, x, y);

    [Fact]
    public void Overlapping_regions_include_owners_past_the_edge_and_interior_keeps_away_from_every_edge()
    {
        Assert.Equal([Cell(0)], Cells.RegionsOverlapping(0, 200, 200, 800, 800));
        Assert.Equal([Cell(-1), Cell(0)], Cells.RegionsOverlapping(0, 50, 500, 50, 500));
        Assert.Equal([Cell(0, 0, 3), Cell(0, 1, 3), Cell(1, 0, 3), Cell(1, 1, 3)],
            Cells.RegionsOverlapping(3, 950, 950, 1050, 1050));
        Assert.Throws<ArgumentException>(() => Cells.RegionsOverlapping(0, 10, 0, 0, 0));

        Assert.True(Cells.IsInterior(Cell(0), 0, 500, 500, 400));
        Assert.False(Cells.IsInterior(Cell(0), 0, 500, 500, 501));
        Assert.False(Cells.IsInterior(Cell(0), 0, 1050, 500, 0));
        Assert.False(Cells.IsInterior(Cell(0), 1, 500, 500, 0));
    }

    [Fact]
    public void Regions_index_their_entities_by_cell_and_answer_owned_near_in_ascending_ids()
    {
        var directory = new RegionDirectory<Member>(Cells, cellSize: 250);
        var a = new Member(30, 100, 100);
        var b = new Member(10, 300, 100);
        var c = new Member(20, 990, 100);
        foreach (var m in new[] { a, b, c })
            directory.Add(m.ObjectId, m);
        directory.Add(40, new Member(40, 1200, 100));

        Assert.True(directory.TryCellOf(20, out int instance, out int cx, out int cy));
        Assert.Equal((0, 3, 0), (instance, cx, cy));
        var near = new List<Member>();
        directory.OwnedNear(Cell(0), 200, 100, 150, near);
        Assert.Equal([10, 30], near.Select(m => m.ObjectId));

        c.X = 1150;
        directory.Relocated(20);
        Assert.True(directory.TryCellOf(20, out _, out cx, out _));
        Assert.Equal(4, cx);
        var cells = new List<int>();
        directory.InCells(0, 4, 0, 4, 0, cells);
        Assert.Equal([20, 40], cells.Order());
        near.Clear();
        directory.OwnedNear(Cell(1), 1100, 100, 200, near);
        Assert.Equal([40], near.Select(m => m.ObjectId));
        directory.Verify();

        Assert.Equal([new RegionTransfer(20, Cell(0), Cell(1), 1)], directory.Handoff());
        directory.Verify();
    }

    [Fact]
    public void Verify_names_an_entity_indexed_in_the_wrong_cell()
    {
        var directory = new RegionDirectory<Member>(Cells, cellSize: 250);
        var member = new Member(5, 100, 100);
        directory.Add(5, member);
        member.X = 600;

        var error = Assert.Throws<InvalidOperationException>(directory.Verify);
        Assert.Contains("entity 5 at 600,100 is indexed in cell (0, 0)", error.Message);
    }

    [Fact]
    public void Inside_a_body_the_directory_refuses_other_regions_entities_and_world_wide_changes()
    {
        var directory = new RegionDirectory<Member>(Cells);
        var mine = new Member(1, 100, 0);
        var theirs = new Member(2, 1500, 0);
        directory.Add(1, mine);
        directory.Add(2, theirs);

        using (directory.EnterBody(Cell(0)))
        {
            Assert.Equal(Cell(0), directory.CurrentBody);
            directory.Join(mine, ActiveSets.NeedRegen);
            directory.Leave(mine, ActiveSets.NeedRegen);
            Assert.Contains("region 0:0_0's body changed NeedRegen of entity 2, which is owned by region 0:1_0",
                Assert.Throws<InvalidOperationException>(() => directory.Join(theirs, ActiveSets.NeedRegen)).Message);
            Assert.Contains("placing entity 3 inside region 0:0_0's body",
                Assert.Throws<InvalidOperationException>(() => directory.Add(3, new Member(3, 0, 0))).Message);
            Assert.Throws<InvalidOperationException>(() => directory.Remove(1));
            Assert.Throws<InvalidOperationException>(() => directory.Relocated(1));
            Assert.Throws<InvalidOperationException>(() => directory.Handoff());
            Assert.Throws<InvalidOperationException>(() => directory.CountOf(ActiveSets.NeedRegen));
            Assert.Throws<InvalidOperationException>(() => directory.Snapshot(ActiveSets.NeedRegen));
            Assert.Throws<InvalidOperationException>(() => directory.BorderMembers(500).ToList());
            Assert.Contains("while region 0:0_0's body runs on this thread",
                Assert.Throws<InvalidOperationException>(() => directory.EnterBody(Cell(1))).Message);
        }

        Assert.Null(directory.CurrentBody);
        directory.Join(theirs, ActiveSets.NeedRegen);
        Assert.Equal(1, directory.CountOf(ActiveSets.NeedRegen));
        directory.Verify();
    }

    [Fact]
    public void Border_members_are_the_entities_a_neighbour_query_could_reach()
    {
        var directory = new RegionDirectory<Member>(Cells);
        directory.Add(1, new Member(1, 500, 500));
        directory.Add(2, new Member(2, 950, 500));
        directory.Add(3, new Member(3, 1020, 500));
        directory.Add(4, new Member(4, 1500, 500));

        var borders = directory.BorderMembers(300).ToDictionary(r => r.Region, r => r.Members.Select(m => m.ObjectId).ToList());

        Assert.Equal([2], borders[Cell(0)]);
        Assert.Equal([3], borders[Cell(1)]);
    }

    [Fact]
    public void Border_snapshots_answer_neighbours_within_reach_and_refuse_longer_queries()
    {
        var snapshots = new BorderSnapshots<Presence>(Cells, reach: 400);
        Assert.Equal(300, snapshots.MaxQueryRadius);
        Assert.Equal(-1, snapshots.Tick);
        snapshots.Publish(7, [
            (Cell(0), [new Presence(5, 0, 950, 500)]),
            (Cell(1), [new Presence(9, 0, 1100, 500), new Presence(3, 0, 1040, 520)]),
            (Cell(1, 0, 2), [new Presence(4, 2, 1050, 500)]),
            (Cell(2), [])
        ]);

        Assert.Equal(7, snapshots.Tick);
        Assert.Empty(snapshots.Of(Cell(2)));
        var near = new List<Presence>();
        snapshots.NeighboursNear(Cell(0), 960, 500, 150, near);
        Assert.Equal([3, 9], near.Select(p => p.ObjectId));

        Assert.Throws<ArgumentOutOfRangeException>(() => snapshots.NeighboursNear(Cell(0), 960, 500, 301, near));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BorderSnapshots<Presence>(Cells, reach: 100));
    }

    [Fact]
    public void Every_region_body_runs_in_its_scope_and_scopes_commit_in_ascending_order_after_the_barrier()
    {
        var scopes = new RecordingScopes();
        var scheduler = new RegionTickScheduler(new SimulationClock(100, () => 0), new ParallelRegionExecutor(4),
            new NullObserver(), scopes);
        // One region per worker, so every body must be running at once to pass the barrier.
        var points = Enumerable.Range(0, 4).Select(i => (Id: i + 1, X: (3 - i) * 1000)).ToArray();
        using var barrier = new Barrier(4);
        long pinnedOnWorkers = -1;
        scheduler.AddRegionStage(new RegionStage("both",
            (_, work) =>
            {
                foreach (var (id, x) in points)
                    work.Add(Cells.RegionOf(0, x, 0), id);
            },
            (tick, id) =>
            {
                Assert.Equal(Cells.RegionOf(0, (4 - id) * 1000, 0), RecordingScopes.Current);
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)), "four regions never ran at once");
                Interlocked.Exchange(ref pinnedOnWorkers, scheduler.Clock.NowUnixMs());
                scopes.Ran(RecordingScopes.Current!.Value);
            },
            parallel: true));

        scheduler.Step(0);

        Assert.Equal(100, pinnedOnWorkers);
        Assert.Equal(Enumerable.Range(0, 4).Select(x => Cell(x)), scopes.Committed);
        Assert.All(scopes.Committed, region => Assert.Contains(region, scopes.RanBeforeFirstCommit));
    }

    [Fact]
    public void A_commit_that_throws_is_reported_for_its_region_and_later_regions_still_commit()
    {
        var observer = new FailureObserver();
        var scopes = new RecordingScopes { FailCommitOf = Cell(1) };
        var scheduler = new RegionTickScheduler(new SimulationClock(100, () => 0), SequentialRegionExecutor.Instance,
            observer, scopes);
        scheduler.AddRegionStage(new RegionStage("s",
            (_, work) =>
            {
                for (int i = 0; i < 3; i++)
                    work.Add(Cell(i), i + 1);
            },
            (_, _) => { }));

        scheduler.Step(0);

        Assert.Equal([Cell(0), Cell(2)], scopes.Committed);
        var failure = Assert.Single(observer.Failures);
        Assert.Equal(("s", "0:1_0 commit"), (failure.Stage, failure.Scope));
    }

    sealed class Member(int objectId, int x, int y) : IRegionMember
    {
        public int ObjectId { get; } = objectId;
        public int InstanceId => 0;
        public int X { get; set; } = x;
        public int Y { get; set; } = y;
        public ActiveSets ActiveSets { get; set; }
    }

    sealed record Presence(int ObjectId, int InstanceId, int X, int Y) : IRegionPresence;

    sealed class RecordingScopes : IRegionScopes
    {
        [ThreadStatic] static RegionId? t_current;

        readonly object _lock = new();

        public static RegionId? Current => t_current;

        public RegionId? FailCommitOf { get; init; }

        public List<RegionId> Committed { get; } = [];

        public List<RegionId> RanBeforeFirstCommit { get; } = [];

        public void Ran(RegionId region)
        {
            lock (_lock)
            {
                if (Committed.Count == 0)
                    RanBeforeFirstCommit.Add(region);
            }
        }

        public IRegionScope Open(string stage, TickContext tick, RegionId region) => new Scope(this, region);

        sealed class Scope(RecordingScopes owner, RegionId region) : IRegionScope
        {
            public IDisposable Enter()
            {
                t_current = region;
                return new Exit();
            }

            public void Commit()
            {
                if (owner.FailCommitOf == region)
                    throw new InvalidOperationException($"commit of {region} failed");
                lock (owner._lock)
                    owner.Committed.Add(region);
            }
        }

        sealed class Exit : IDisposable
        {
            public void Dispose() => t_current = null;
        }
    }

    class NullObserver : ISimulationObserver
    {
        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
        }

        public virtual void StageFailed(StageFailure failure) =>
            throw new InvalidOperationException(failure.ToString(), failure.Error);

        public void TicksSkipped(TickContext tick, long skipped)
        {
        }

        public void EntitiesHandedOff(TickContext tick, IReadOnlyList<RegionTransfer> transfers)
        {
        }

        public void StepCompleted(TickContext tick, TimeSpan elapsed, TimeSpan budget)
        {
        }
    }

    sealed class FailureObserver : NullObserver
    {
        public List<StageFailure> Failures { get; } = [];

        public override void StageFailed(StageFailure failure) => Failures.Add(failure);
    }
}
