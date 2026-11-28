using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Bosses;
using Fineage.Zone.Clan;
using Fineage.Zone.Lottery;
using Fineage.Zone.Manor;
using Fineage.Zone.Olympiad;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Siege;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.Time;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Issue #983: one explicit time model. Simulation time follows the monotonic side of <see cref="IZoneTime"/>, calendar
/// schedules read the step's wall sample, and persisted absolute UTC timers catch up by an explicit one-shot policy.
/// </summary>
[Trait("Category", "Fast")]
public sealed class TimeModelTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 26, 17, 0, 0, TimeSpan.Zero); // Monday

    [Fact]
    public void Simulation_ticks_follow_monotonic_time_across_forward_and_backward_wall_jumps()
    {
        var time = new SplitZoneTime(T0, timestamp: 1_000);
        var clock = new SimulationClock(200, time);
        var scheduler = new RegionTickScheduler(clock, SequentialRegionExecutor.Instance, NoopObserver.Instance);
        var seen = new List<TickContext>();
        scheduler.AddWorldStage("seen", seen.Add);

        long startTick = scheduler.Tick;
        time.AdvanceMonotonic(200);
        time.JumpWall(TimeSpan.FromMilliseconds(200));
        scheduler.Step(time.UtcNow.ToUnixTimeMilliseconds());
        Assert.Equal(startTick + 1, scheduler.Tick);

        // A wall clock changed far forward must not make the combat/cooldown tick jump.
        time.AdvanceMonotonic(200);
        time.JumpWall(TimeSpan.FromDays(1));
        scheduler.Step(time.UtcNow.ToUnixTimeMilliseconds());
        Assert.Equal(startTick + 2, scheduler.Tick);
        Assert.Equal(clock.TimeOf(startTick + 2), seen[^1].NowUnixMs);
        Assert.Equal(time.UtcNow.ToUnixTimeMilliseconds(), seen[^1].WallUnixMs);

        // A wall clock changed backwards must not stall or rewind simulation time either.
        time.AdvanceMonotonic(200);
        time.JumpWall(TimeSpan.FromDays(-2));
        scheduler.Step(time.UtcNow.ToUnixTimeMilliseconds());
        Assert.Equal(startTick + 3, scheduler.Tick);
        Assert.Equal(clock.TimeOf(startTick + 3), seen[^1].NowUnixMs);
    }

    [Fact]
    public void Boss_respawn_is_absolute_utc_and_survives_restart_without_rerolling()
    {
        var store = new MemoryBossStore();
        var catalog = BossCatalog.From([Boss()]);
        var before = new BossSchedule(catalog, store, T0.ToUnixTimeMilliseconds());
        Assert.True(before.OnBossKilled(25_001, T0.ToUnixTimeMilliseconds(), new MinBossRandom()));
        Assert.True(before.TryView(25_001, out var killed));
        long due = killed.RespawnUnixMs;
        Assert.True(due > T0.ToUnixTimeMilliseconds());

        // Restart while the wall clock reads earlier than the stored UTC deadline: the deadline is data, not a
        // duration restarted from boot.
        var restarted = new BossSchedule(catalog, store, due - 1);
        Assert.True(restarted.TryView(25_001, out var waiting));
        Assert.Equal(BossLifeStatus.Dead, waiting.Status);
        Assert.Equal(due, waiting.RespawnUnixMs);

        var overdue = new BossSchedule(catalog, store, due + 1);
        Assert.True(overdue.TryView(25_001, out var spawned));
        Assert.Equal(BossLifeStatus.Alive, spawned.Status);
        Assert.Contains(overdue.StartupOrders(), order => order.NpcId == 25_001);
    }

    [Fact]
    public void Seven_signs_restart_keeps_the_next_boundary_and_catches_a_missed_period_once()
    {
        var store = new MemorySevenSignsStore();
        var options = new SevenSignsOptions { PeriodStartDay = DayOfWeek.Monday, PeriodStartHour = 18 };
        var engine = new SevenSignsEngine(T0, options, store);
        long boundary = engine.NextChange.ToUnixTimeMilliseconds();
        Assert.True(boundary > T0.ToUnixTimeMilliseconds());
        engine.Save();

        DateTimeOffset restartedAt = T0.AddDays(8);
        var restarted = new SevenSignsEngine(restartedAt, options, store);
        Assert.Equal(boundary, restarted.NextChange.ToUnixTimeMilliseconds());

        var notices = new List<SevenSignsNotice>();
        Assert.True(restarted.TryAdvance(restartedAt, notices));
        Assert.Single(notices);
        Assert.True(restarted.NextChange > restartedAt);
        Assert.False(restarted.TryAdvance(restartedAt, notices));
    }

    [Fact]
    public void Olympiad_restart_closes_one_missed_period_and_does_not_replay_it_again()
    {
        var store = new MemoryOlympiadStore();
        var settings = new OlympiadSettings { Enabled = true };
        var board = new OlympiadBoard(T0, settings, store);
        long end = board.View(0, T0.ToUnixTimeMilliseconds()).OlympiadEndUnixMs;
        Assert.True(end > T0.ToUnixTimeMilliseconds());

        DateTimeOffset restartedAt = DateTimeOffset.FromUnixTimeMilliseconds(end).AddDays(1);
        var restarted = new OlympiadBoard(restartedAt, settings, store);
        long newEnd = restarted.View(0, restartedAt.ToUnixTimeMilliseconds()).OlympiadEndUnixMs;
        Assert.True(newEnd > restartedAt.ToUnixTimeMilliseconds());

        var again = new OlympiadBoard(restartedAt, settings, store);
        Assert.Equal(newEnd, again.View(0, restartedAt.ToUnixTimeMilliseconds()).OlympiadEndUnixMs);
    }

    [Fact]
    public void Siege_restart_after_downtime_reschedules_once_into_the_future()
    {
        var board = new SiegeBoard(T0.ToUnixTimeMilliseconds());
        var castles = board.ExportCastles();
        DateTimeOffset restartedAt = T0.AddDays(60);

        var restarted = new SiegeBoard(T0.ToUnixTimeMilliseconds());
        restarted.Restore(castles, [], restartedAt.ToUnixTimeMilliseconds());

        Assert.All(restarted.Castles, castle =>
        {
            Assert.False(castle.InProgress);
            Assert.True(castle.SiegeUnixMs > restartedAt.ToUnixTimeMilliseconds(),
                $"castle {castle.Id} must be rescheduled, not replayed at {castle.SiegeUnixMs}");
        });
    }

    [Fact]
    public void Manor_mode_and_next_change_are_derived_from_wall_after_a_jump()
    {
        var lateEvening = new DateTimeOffset(2026, 10, 26, 19, 59, 0, TimeSpan.Zero);
        Assert.Equal(ManorProtocol.ModeApproved, ManorRules.ModeAt(lateEvening.ToUnixTimeMilliseconds(), 0));

        DateTimeOffset afterJump = lateEvening.AddHours(2);
        int mode = ManorRules.ModeAt(afterJump.ToUnixTimeMilliseconds(), 0);
        Assert.Equal(ManorProtocol.ModeModifiable, mode);
        Assert.True(ManorRules.NextChange(mode, afterJump.ToUnixTimeMilliseconds(), 0) >
                    afterJump.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Lottery_after_three_missed_draws_draws_once_and_schedules_the_next_from_now()
    {
        var board = new LotteryBoard(T0.ToUnixTimeMilliseconds());
        long due = board.EndUnixMs;
        long now = DateTimeOffset.FromUnixTimeMilliseconds(due).AddDays(21).ToUnixTimeMilliseconds();

        var notices = new List<LotteryNotice>();
        board.Tick(now, new Random(1), notices);

        Assert.Equal(2, board.Number);
        Assert.True(board.EndUnixMs > now, "missed draws are skipped, not replayed at boot");
        board.Tick(now, new Random(1), notices);
        Assert.Equal(2, board.Number);
    }

    [Fact]
    public void Hall_auction_due_is_read_from_absolute_utc_and_fires_once()
    {
        long now = T0.ToUnixTimeMilliseconds();
        var actor = new HallAuctionActor
        {
            Halls = new Dictionary<int, ClanHallSave>
            {
                [1] = new() { Id = 1, OwnerClanId = 0, AuctionEndUnixMs = now - 1 }
            }
        };
        var tick = new TickContext(1, now, now);

        Assert.Equal(1, Assert.Single(actor.Step(tick)).HallId);
        actor.Halls[1].AuctionEndUnixMs = now + 7L * 24 * 3600 * 1000;
        Assert.Empty(actor.Step(tick));
    }

    [Fact]
    public void Regular_npc_respawn_keeps_its_absolute_simulation_deadline_across_wall_jumps()
    {
        var (catalog, row) = SpawnFixture(respawnSeconds: 30);
        var time = new SplitZoneTime(T0, timestamp: 1_000);
        var world = new ZoneWorld(spawnCatalog: catalog, time: time);

        Assert.Contains(row.ObjectId, world.NpcObjectIds);
        world.DespawnNpcForTests(row.ObjectId);
        Assert.DoesNotContain(row.ObjectId, world.NpcObjectIds);

        // Mobius Spawn.decreaseCount stores an absolute deadline. Fineage measures it on the simulation clock, so a
        // wall jump is not progress: the slot stays dead until the simulation deadline is reached.
        long due = Assert.NotNull(world.SpawnRespawnDueUnixMsForTests(row.ObjectId));
        Assert.Equal(T0.AddSeconds(30).ToUnixTimeMilliseconds(), due);
        time.JumpWall(TimeSpan.FromHours(6));
        time.AdvanceMonotonic(29_000);
        world.AdvanceSpawnsForTests();
        Assert.DoesNotContain(row.ObjectId, world.NpcObjectIds);
        Assert.Equal(1, world.PendingSpawnRespawnsForTests);

        time.AdvanceMonotonic(1_000);
        world.AdvanceSpawnsForTests();
        Assert.Contains(row.ObjectId, world.NpcObjectIds);
        Assert.Equal(0, world.PendingSpawnRespawnsForTests);
    }

    [Fact]
    public void Regular_npc_respawn_restart_rebuilds_the_spawn_table_like_mobius_spawn_init()
    {
        var (catalog, row) = SpawnFixture(respawnSeconds: 30);
        var first = new ZoneWorld(spawnCatalog: catalog, time: new SplitZoneTime(T0, timestamp: 1_000));
        first.DespawnNpcForTests(row.ObjectId);
        Assert.Equal(1, first.PendingSpawnRespawnsForTests);

        // Mobius keeps pending respawns only in RespawnTaskManager's memory and SpawnTable calls Spawn.init() at
        // boot, which spawns the full table. A restart before the old deadline therefore shows the NPC immediately
        // and starts with no carried-over timer; a spawn-timer sidecar would be a parity change, not a fix.
        var restarted = new ZoneWorld(spawnCatalog: catalog, time: new SplitZoneTime(T0.AddSeconds(5), timestamp: 5_000));
        Assert.Contains(row.ObjectId, restarted.NpcObjectIds);
        Assert.Equal(0, restarted.PendingSpawnRespawnsForTests);
        Assert.Null(restarted.SpawnRespawnDueUnixMsForTests(row.ObjectId));
    }

    [Fact]
    public void Manor_restart_enters_the_wall_clock_period_and_schedules_the_next_boundary_from_it()
    {
        var board = new ManorBoard(ManorData.Empty, new ManorSettings { Enabled = true });

        // Stale values from a previous process are not authoritative: Mobius CastleManorManager derives mode and
        // next mode change in its constructor from Calendar.getInstance().
        board.Mode = ManorProtocol.ModeApproved;
        board.NextChangeMs = 1;
        var afterMaintenance = new DateTimeOffset(2026, 10, 26, 20, 10, 0, TimeSpan.Zero);
        board.RestoreFromWall(afterMaintenance.ToUnixTimeMilliseconds());

        Assert.Equal(ManorProtocol.ModeModifiable, board.Mode);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 4, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            board.NextChangeMs);

        var duringMaintenance = new DateTimeOffset(2026, 10, 27, 20, 3, 0, TimeSpan.Zero);
        board.RestoreFromWall(duringMaintenance.ToUnixTimeMilliseconds());
        Assert.Equal(ManorProtocol.ModeMaintenance, board.Mode);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 20, 6, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            board.NextChangeMs);

        var disabled = new ManorBoard(ManorData.Empty, new ManorSettings { Enabled = false });
        disabled.RestoreFromWall(duringMaintenance.ToUnixTimeMilliseconds());
        Assert.Equal(ManorProtocol.ModeDisabled, disabled.Mode);
        Assert.Equal(0, disabled.NextChangeMs);
    }

    [Fact]
    public void Manor_mode_change_runs_on_the_step_wall_sample_not_the_simulation_tick()
    {
        var board = new ManorBoard(ManorData.Empty, new ManorSettings { Enabled = true });
        var wallBefore = new DateTimeOffset(2026, 10, 26, 19, 59, 0, TimeSpan.Zero);
        board.RestoreFromWall(wallBefore.ToUnixTimeMilliseconds());
        Assert.Equal(ManorProtocol.ModeApproved, board.Mode);

        var actor = new ManorActor(board);
        long wall = wallBefore.AddMinutes(1).ToUnixTimeMilliseconds();
        long simulation = wallBefore.AddHours(5).ToUnixTimeMilliseconds();
        var change = Assert.Single(actor.Step(new TickContext(1, simulation, wall)));

        Assert.Equal(ManorProtocol.ModeApproved, change.From);
        Assert.Equal(ManorProtocol.ModeMaintenance, change.To);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 20, 6, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            board.NextChangeMs);
    }

    static (SpawnCatalog Catalog, SpawnNpcDefinition Row) SpawnFixture(int respawnSeconds)
    {
        var gremlin = TalkingIsland.Gremlins[0];
        var row = new SpawnNpcDefinition(
            gremlin.ObjectId,
            gremlin.NpcId,
            gremlin.Name,
            gremlin.X,
            gremlin.Y,
            gremlin.Z,
            gremlin.Heading,
            gremlin.Attackable,
            TalkingIsland.GremlinMaxHp,
            gremlin.RunSpeed,
            gremlin.WalkSpeed,
            RespawnDelaySeconds: respawnSeconds,
            RespawnRandomSeconds: 0,
            Title: gremlin.Title ?? "",
            NpcType: string.IsNullOrEmpty(gremlin.NpcType) ? "Monster" : gremlin.NpcType);
        var catalog = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [SpawnCatalog.DefaultMapId] = new(SpawnCatalog.DefaultMapId, Enabled: true, [row])
        });
        return (catalog, row);
    }

    static BossDefinition Boss()
    {
        var (min, max) = BossCatalog.RaidWindow(1200, 600);
        return new BossDefinition(
            25_001, BossKind.Raid, Enabled: true, 9_700_001, "Clock Boss", "Raid Boss", "RaidBoss",
            1, 2, 3, 0, true, 100, 40, 170, 50, min, max, BossLifeStatus.Alive);
    }

    sealed class MinBossRandom : IBossRandom
    {
        public long NextInclusive(long minInclusive, long maxInclusive) => minInclusive;
    }

    sealed class SplitZoneTime : IZoneTime
    {
        long _timestamp;

        public SplitZoneTime(DateTimeOffset utcNow, long timestamp)
        {
            UtcNow = utcNow;
            _timestamp = timestamp;
        }

        public DateTimeOffset UtcNow { get; private set; }

        public long Timestamp => _timestamp;

        public bool SimulationFollowsWall => false;

        public TimeSpan Elapsed(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromMilliseconds(endingTimestamp - startingTimestamp);

        public void AdvanceMonotonic(long milliseconds) => _timestamp += milliseconds;

        public void JumpWall(TimeSpan delta) => UtcNow += delta;
    }

    sealed class NoopObserver : ISimulationObserver
    {
        public static readonly NoopObserver Instance = new();

        public void StageCompleted(string stage, TickContext tick, int regions, int entities, TimeSpan elapsed)
        {
        }

        public void StageFailed(StageFailure failure) => throw new Xunit.Sdk.XunitException(failure.ToString());

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
}
