using Fineage.Zone.Bosses;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneBossScheduleTests
{
    [Fact]
    public void Enabled_boss_spawns_on_start_dies_and_returns_on_the_clock()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var schedule = new BossSchedule(
            BossCatalog.From([Boss(enabled: true)]),
            new MemoryBossStore(),
            () => clock.Now,
            new MinRandom());
        Assert.True(schedule.TryNoteVitals(25001, 15, 9));

        var world = World(clock, schedule);
        Assert.Contains(80001, world.NpcObjectIds);
        Assert.Equal((15, 9), world.BossVitalsForTests(25001));

        Assert.True(world.OnBossKilled(25001));
        Assert.DoesNotContain(80001, world.NpcObjectIds);
        Assert.False(world.OnBossKilled(42));

        clock.Advance(TimeSpan.FromMilliseconds(43_200_000 - 1));
        world.AdvanceBossesForTests();
        Assert.DoesNotContain(80001, world.NpcObjectIds);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        world.AdvanceBossesForTests();
        Assert.Contains(80001, world.NpcObjectIds);
        Assert.Equal((100, 40), world.BossVitalsForTests(25001));
    }

    [Fact]
    public void Disabled_boss_stays_out_of_the_world_when_its_timer_elapses()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var schedule = new BossSchedule(
            BossCatalog.From([Boss(enabled: false)]),
            new MemoryBossStore(),
            () => clock.Now,
            new MinRandom());
        var world = World(clock, schedule);
        Assert.DoesNotContain(80001, world.NpcObjectIds);

        Assert.True(world.OnBossKilled(25001));
        clock.Advance(TimeSpan.FromHours(12));
        world.AdvanceBossesForTests();
        Assert.DoesNotContain(80001, world.NpcObjectIds);
        Assert.True(schedule.TryView(25001, out var view));
        Assert.Equal(BossLifeStatus.Alive, view.Status);
    }

    [Fact]
    public void Datapack_bosses_are_not_added_to_the_default_world()
    {
        var world = new ZoneWorld();
        Assert.DoesNotContain(BossCatalog.FirstObjectId, world.NpcObjectIds);
    }

    static ZoneWorld World(FakeClock clock, BossSchedule schedule) =>
        new(
            spawnCatalog: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
            {
                [SpawnCatalog.DefaultMapId] = new(
                    SpawnCatalog.DefaultMapId,
                    true,
                    [new SpawnNpcDefinition(50001, 20001, "Gremlin", 1, 2, 3, 0, true, 96, 80, 40, 60, 0)])
            }),
            clock: () => clock.Now,
            bosses: schedule);

    static BossDefinition Boss(bool enabled)
    {
        var (min, max) = BossCatalog.RaidWindow(129600, 86400);
        return new BossDefinition(
            25001,
            BossKind.Raid,
            enabled,
            80001,
            "Greyclaw Kutus",
            "Raid Boss",
            "RaidBoss",
            -54416,
            146480,
            -2887,
            0,
            true,
            100,
            40,
            170,
            50,
            min,
            max,
            BossLifeStatus.Alive);
    }

    sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;

        public void Advance(TimeSpan delta) => Now += delta;
    }

    sealed class MinRandom : IBossRandom
    {
        public long NextInclusive(long minInclusive, long maxInclusive) => minInclusive;
    }
}
