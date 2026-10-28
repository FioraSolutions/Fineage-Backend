using Fineage.Protocol;
using Fineage.Zone.Bosses;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class BossScheduleTests
{
    const int RaidId = 25001;
    const int GrandId = 29001;
    const int ZeroId = 25333;

    [Fact]
    public void Catalog_loads_interlude_rows_disabled_outside_talking_island()
    {
        var catalog = BossCatalog.LoadDefault();

        Assert.Equal(223, catalog.Bosses.Count);
        Assert.Equal(215, catalog.Bosses.Count(boss => boss.Kind == BossKind.Raid));
        Assert.Equal(8, catalog.Bosses.Count(boss => boss.Kind == BossKind.Grand));
        Assert.All(catalog.Bosses, boss => Assert.False(boss.Enabled));
        Assert.Equal(BossCatalog.FirstObjectId, catalog.Get(RaidId).ObjectId);
        Assert.Equal(BossCatalog.FirstObjectId + catalog.Bosses.Count - 1, catalog.Bosses[^1].ObjectId);
        Assert.Equal(80223, catalog.Get(29096).ObjectId);

        var kutus = catalog.Get(RaidId);
        Assert.Equal("Greyclaw Kutus", kutus.Name);
        Assert.Equal((-54416, 146480, -2887), (kutus.X, kutus.Y, kutus.Z));
        Assert.Equal((43_200_000L, 216_000_000L), (kutus.RespawnMinMillis, kutus.RespawnMaxMillis));
        Assert.Equal(40330, kutus.MaxHp);

        var queen = catalog.Get(GrandId);
        Assert.Equal(BossKind.Grand, queen.Kind);
        Assert.Equal(BossLifeStatus.Alive, queen.InitialStatus);
        Assert.Equal((100_800_000L, 158_400_000L), (queen.RespawnMinMillis, queen.RespawnMaxMillis));
        Assert.Equal((-21610, 181594, -5734), (queen.X, queen.Y, queen.Z));

        var antharas = catalog.Get(29019);
        Assert.Equal((921_600_000L, 979_200_000L), (antharas.RespawnMinMillis, antharas.RespawnMaxMillis));

        var eilhalder = catalog.Get(25328);
        Assert.Equal((7_200_000L, 14_400_000L), (eilhalder.RespawnMinMillis, eilhalder.RespawnMaxMillis));

        var kerinne = catalog.Get(29030);
        Assert.Equal(604_800_000L, kerinne.RespawnMinMillis);
        Assert.Equal(kerinne.RespawnMinMillis, kerinne.RespawnMaxMillis);

        var anakazel = catalog.Get(ZeroId);
        Assert.False(anakazel.HasRespawnWindow);
        Assert.Equal((0, 0, 0), (anakazel.X, anakazel.Y, anakazel.Z));
    }

    [Fact]
    public void OnBossKilled_rolls_inclusive_raid_and_grand_windows()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var random = new BoundRandom(high: false);
        var schedule = Schedule(clock, random, raidEnabled: true, grandEnabled: false);

        Assert.True(schedule.OnBossKilled(RaidId));
        var raid = View(schedule, RaidId);
        Assert.Equal(BossLifeStatus.Dead, raid.Status);
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds() + 43_200_000L, raid.RespawnUnixMs);
        Assert.Equal(100, raid.CurrentHp);

        random.High = true;
        Assert.True(schedule.OnBossKilled(GrandId));
        var grand = View(schedule, GrandId);
        Assert.Equal(BossLifeStatus.Dead, grand.Status);
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds() + 158_400_000L, grand.RespawnUnixMs);
        Assert.False(schedule.OnBossKilled(999999));
    }

    [Fact]
    public void Raid_multiplier_scales_the_window_before_the_roll()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var schedule = Schedule(clock, new BoundRandom(high: false), raidEnabled: true, grandEnabled: false, raidMin: 2, raidMax: 2);

        Assert.True(schedule.OnBossKilled(RaidId));
        Assert.Equal(86_400_000L, View(schedule, RaidId).RespawnUnixMs);
    }

    [Fact]
    public void Restart_keeps_a_future_corpse_and_spawns_when_the_time_has_passed()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch.AddHours(1));
        var store = new MemoryBossStore();
        var first = Schedule(clock, new BoundRandom(high: false), raidEnabled: true, grandEnabled: true, store);
        Assert.True(first.OnBossKilled(RaidId));
        long due = View(first, RaidId).RespawnUnixMs;

        clock.Advance(TimeSpan.FromMilliseconds(43_200_000 - 1));
        var waiting = Schedule(clock, new BoundRandom(high: false), raidEnabled: true, grandEnabled: true, store);
        Assert.Equal(BossLifeStatus.Dead, View(waiting, RaidId).Status);
        Assert.DoesNotContain(waiting.StartupOrders(), order => order.NpcId == RaidId);

        clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert.True(clock.Now.ToUnixTimeMilliseconds() > due);
        var spawned = Schedule(clock, new BoundRandom(high: false), raidEnabled: true, grandEnabled: true, store);
        Assert.Equal(BossLifeStatus.Alive, View(spawned, RaidId).Status);
        Assert.Equal(0, View(spawned, RaidId).RespawnUnixMs);
        Assert.Contains(spawned.StartupOrders(), order => order.NpcId == RaidId && order.CurrentHp == 100);
    }

    [Fact]
    public void Due_disabled_boss_becomes_alive_without_a_spawn_order()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var schedule = Schedule(clock, new BoundRandom(high: false), raidEnabled: false, grandEnabled: false);
        Assert.True(schedule.OnBossKilled(RaidId));
        clock.Advance(TimeSpan.FromMilliseconds(43_200_000));

        var due = schedule.CollectDue();
        Assert.Empty(due);
        Assert.Equal(BossLifeStatus.Alive, View(schedule, RaidId).Status);
        Assert.Equal(0, View(schedule, RaidId).RespawnUnixMs);
    }

    [Fact]
    public void Zero_delay_boss_stays_dead_until_the_next_process_start()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch.AddDays(1));
        var store = new MemoryBossStore();
        var schedule = Schedule(clock, new BoundRandom(high: false), raidEnabled: true, grandEnabled: false, store);
        Assert.True(schedule.OnBossKilled(ZeroId));
        clock.Advance(TimeSpan.FromDays(3));
        Assert.Empty(schedule.CollectDue());
        Assert.Equal(BossLifeStatus.Dead, View(schedule, ZeroId).Status);

        var restarted = Schedule(clock, new BoundRandom(high: false), raidEnabled: true, grandEnabled: false, store);
        Assert.Equal(BossLifeStatus.Alive, View(restarted, ZeroId).Status);
        Assert.Contains(restarted.StartupOrders(), order => order.NpcId == ZeroId);
    }

    [Fact]
    public void Alive_vitals_round_trip_and_fighting_spawns_while_waiting_does_not()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var store = new MemoryBossStore();
        var schedule = Schedule(clock, new BoundRandom(high: true), raidEnabled: true, grandEnabled: true, store);
        Assert.True(schedule.TryNoteVitals(RaidId, 12, 0));
        Assert.Equal(12, View(schedule, RaidId).CurrentHp);
        Assert.Equal(40, View(schedule, RaidId).CurrentMp);

        var restored = Schedule(clock, new BoundRandom(high: true), raidEnabled: true, grandEnabled: true, store);
        Assert.Equal(12, View(restored, RaidId).CurrentHp);
        Assert.Equal(40, View(restored, RaidId).CurrentMp);
        Assert.Contains(restored.StartupOrders(), order => order.NpcId == RaidId && order.CurrentHp == 12 && order.CurrentMp == 40);

        var waiting = new BossSchedule(
            BossCatalog.From([GrandBoss(GrandId, 81001, enabled: true, BossLifeStatus.Waiting)]),
            new MemoryBossStore(),
            () => clock.Now,
            new BoundRandom(high: false));
        Assert.Empty(waiting.StartupOrders());

        var fighting = new BossSchedule(
            BossCatalog.From([GrandBoss(GrandId, 81001, enabled: true, BossLifeStatus.Fighting)]),
            new MemoryBossStore(),
            () => clock.Now,
            new BoundRandom(high: false));
        Assert.Contains(fighting.StartupOrders(), order => order.NpcId == GrandId);
    }

    [Fact]
    public void Force_spawn_clears_a_dead_boss()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var schedule = Schedule(clock, new BoundRandom(high: false), raidEnabled: false, grandEnabled: false);
        Assert.True(schedule.OnBossKilled(RaidId));
        Assert.True(schedule.TryForceSpawn(RaidId, out var order));
        Assert.True(order.IgnoreEnabled);
        Assert.Equal(BossLifeStatus.Alive, View(schedule, RaidId).Status);
        Assert.Equal(0, View(schedule, RaidId).RespawnUnixMs);
        Assert.False(schedule.TryForceSpawn(42, out _));
    }

    [Fact]
    public void Boss_status_message_round_trips_keys()
    {
        var message = new BossStatusMsg
        {
            Bosses =
            [
                new BossStatusEntryMsg
                {
                    NpcId = GrandId,
                    ObjectId = 80100,
                    Kind = BossProtocol.KindGrand,
                    Status = BossProtocol.StatusDead,
                    RespawnUnixMs = 50,
                    CurrentHp = 9,
                    MaxHp = 10,
                    CurrentMp = 3,
                    MaxMp = 4,
                    Enabled = false,
                    Spawned = true
                }
            ]
        };

        var copy = MessagePackSerializer.Deserialize<BossStatusMsg>(MessagePackSerializer.Serialize(message));
        var row = Assert.Single(copy.Bosses);
        Assert.Equal(GrandId, row.NpcId);
        Assert.Equal(80100, row.ObjectId);
        Assert.Equal(BossProtocol.KindGrand, row.Kind);
        Assert.Equal(BossProtocol.StatusDead, row.Status);
        Assert.Equal(50, row.RespawnUnixMs);
        Assert.Equal(9, row.CurrentHp);
        Assert.Equal(10, row.MaxHp);
        Assert.Equal(3, row.CurrentMp);
        Assert.Equal(4, row.MaxMp);
        Assert.False(row.Enabled);
        Assert.True(row.Spawned);
        Assert.Equal(BossProtocol.StatusDead, (int)BossLifeStatus.Dead);
        Assert.Equal(93, (int)MsgType.BossStatusRequest);
        Assert.Equal(94, (int)MsgType.AdminBossSchedule);
        Assert.Equal(1093, (int)MsgType.BossStatus);
        Assert.Equal((ushort)MsgType.BossStatusRequest, (ushort)Fineage.Protocol.V2.MsgType.BossStatusRequest);
        Assert.Equal((ushort)MsgType.BossStatus, (ushort)Fineage.Protocol.V2.MsgType.BossStatus);
    }

    static BossSchedule Schedule(
        FakeClock clock,
        IBossRandom random,
        bool raidEnabled,
        bool grandEnabled,
        IBossStore? store = null,
        double raidMin = 1,
        double raidMax = 1) =>
        new(
            BossCatalog.From(
            [
                Raid(RaidId, 80001, raidEnabled, 129600, 86400),
                Raid(ZeroId, 80002, raidEnabled, 0, 0),
                GrandBoss(GrandId, 80003, grandEnabled, BossLifeStatus.Alive)
            ]),
            store ?? new MemoryBossStore(),
            () => clock.Now,
            random,
            raidMin,
            raidMax);

    static BossView View(BossSchedule schedule, int npcId)
    {
        Assert.True(schedule.TryView(npcId, out var view));
        return view;
    }

    static BossDefinition Raid(int npcId, int objectId, bool enabled, int delay, int random)
    {
        var (min, max) = BossCatalog.RaidWindow(delay, random);
        return new BossDefinition(
            npcId,
            BossKind.Raid,
            enabled,
            objectId,
            "Raid",
            "Raid Boss",
            "RaidBoss",
            10,
            20,
            30,
            0,
            true,
            100,
            40,
            120,
            50,
            min,
            max,
            BossLifeStatus.Alive);
    }

    static BossDefinition GrandBoss(int npcId, int objectId, bool enabled, BossLifeStatus initial)
    {
        var (min, max) = BossCatalog.GrandWindow(36, 8);
        return new BossDefinition(
            npcId,
            BossKind.Grand,
            enabled,
            objectId,
            "Queen Ant",
            "",
            "GrandBoss",
            -21610,
            181594,
            -5734,
            0,
            true,
            200,
            80,
            170,
            60,
            min,
            max,
            initial);
    }

    sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;

        public void Advance(TimeSpan delta) => Now += delta;
    }

    sealed class BoundRandom(bool high) : IBossRandom
    {
        public bool High { get; set; } = high;

        public long NextInclusive(long minInclusive, long maxInclusive)
        {
            if (minInclusive >= maxInclusive)
                return minInclusive;
            return High ? maxInclusive : minInclusive;
        }
    }
}
