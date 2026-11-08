using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Bosses;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Boss schedule over Net: the status rows a client asks for, the admin dead / force-spawn actions, and the
/// <c>FINEAGE_ZONE_ADMIN</c> gate. The world <see cref="ZoneWorld"/> is driven through <see cref="WorldBench"/>,
/// so these assert on the recorded <see cref="Fineage.Zone.World.IWorldEventPublisher"/> fanout rather than a frame.
/// </summary>
public sealed class NetBossTests
{
    /// <summary>MinRandom: every respawn window opens at its lower bound, so the assertion is exact.</summary>
    sealed class MinRandom : IBossRandom
    {
        public long NextInclusive(long minInclusive, long maxInclusive) => minInclusive;
    }

    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    static (WorldBench Bench, NetBridgeRecorder Rec) World()
    {
        var clock = Now;
        var schedule = new BossSchedule(
            BossCatalog.From([Boss(enabled: true)]),
            new MemoryBossStore(),
            () => clock,
            new MinRandom());
        var bench = new WorldBench(clock: () => clock, bosses: schedule);
        return (bench, bench.Rec);
    }

    static Fineage.Protocol.BossStatusMsg LastStatus(NetBridgeRecorder rec) =>
        Assert.IsType<Fineage.Protocol.BossStatusMsg>(rec.Of("FanoutBossStatus").Last()[1]);

    [Fact]
    public async Task Status_returns_the_row_and_admin_actions_flip_it()
    {
        var (bench, rec) = World();
        BenchPlayer actor = await bench.EnterAsync("boss", "Scout", x: BossX, y: BossY, z: BossZ);

        Assert.Null(await bench.World.TryBossStatusAsync(actor.Session, BossNpcId, CancellationToken.None));
        var row = Assert.Single(LastStatus(rec).Bosses);
        Assert.Equal(BossNpcId, row.NpcId);
        Assert.Equal(BossProtocol.KindRaid, row.Kind);
        Assert.Equal(BossProtocol.StatusAlive, row.Status);
        Assert.Equal(BossObjectId, row.ObjectId);
        Assert.False(row.Enabled is false);
        Assert.True(row.Spawned);
        Assert.Equal(0, row.RespawnUnixMs);
        Assert.Equal(row.MaxHp, row.CurrentHp);

        // Mark dead: the world removes the pawn and opens the respawn window.
        Assert.Null(await bench.World.TryAdminBossNetAsync(
            actor.Session, new AdminBossScheduleNetMsg { NpcId = BossNpcId, Action = BossProtocol.ActionMarkDead }, CancellationToken.None));
        Assert.Contains(rec.Of("FanoutEntityDespawn"), _ => true);

        Assert.Null(await bench.World.TryBossStatusAsync(actor.Session, BossNpcId, CancellationToken.None));
        var dead = Assert.Single(LastStatus(rec).Bosses);
        Assert.Equal(BossProtocol.StatusDead, dead.Status);
        Assert.False(dead.Spawned);
        long expected = Now.ToUnixTimeMilliseconds() + BossCatalog.RaidWindow(1200, 600).MinMillis;
        Assert.Equal(expected, dead.RespawnUnixMs);

        // Force spawn: the row is alive again and the pawn is back with its full vitals.
        Assert.Null(await bench.World.TryAdminBossNetAsync(
            actor.Session, new AdminBossScheduleNetMsg { NpcId = BossNpcId, Action = BossProtocol.ActionForceSpawn }, CancellationToken.None));
        Assert.Null(await bench.World.TryBossStatusAsync(actor.Session, BossNpcId, CancellationToken.None));
        var back = Assert.Single(LastStatus(rec).Bosses);
        Assert.Equal(BossProtocol.StatusAlive, back.Status);
        Assert.True(back.Spawned);
        Assert.Equal(0, back.RespawnUnixMs);
    }

    [Fact]
    public async Task Asking_for_every_boss_lists_the_catalog_and_an_unknown_id_is_refused()
    {
        var (bench, rec) = World();
        BenchPlayer actor = await bench.EnterAsync("boss", "Scout", x: BossX, y: BossY, z: BossZ);

        Assert.Null(await bench.World.TryBossStatusAsync(actor.Session, 0, CancellationToken.None));
        Assert.Equal(BossNpcId, Assert.Single(LastStatus(rec).Bosses).NpcId);

        Assert.Equal(BossProtocol.UnknownBoss, await bench.World.TryBossStatusAsync(actor.Session, 1, CancellationToken.None));
        Assert.Equal(BossProtocol.UnknownBoss, await bench.World.TryAdminBossNetAsync(
            actor.Session, new AdminBossScheduleNetMsg { NpcId = 1, Action = BossProtocol.ActionMarkDead }, CancellationToken.None));
        Assert.Equal("bad_request", await bench.World.TryAdminBossNetAsync(
            actor.Session, new AdminBossScheduleNetMsg { NpcId = BossNpcId, Action = 9 }, CancellationToken.None));
    }

    /// <summary>
    /// The admin gate itself lives on the Net listener, which is the only place that knows the env flag, so this
    /// drives a real connection: admin off means <c>forbidden</c> and no schedule change.
    /// </summary>
    [Fact]
    public async Task Admin_schedule_is_forbidden_when_admin_is_off()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var host = await NetZoneHost.StartAsync();
        await using var session = await host.ConnectAsync();
        await session.HelloAsync(timeout.Token);
        await session.AuthAsync("boss", "Scout", timeout.Token);
        await session.EnterWorldAsync(timeout.Token);

        string code = await session.RefusalCodeAsync(
            NetOpcode.AdminBossSchedule,
            NetCodec.EncodeAdminBossSchedule(new AdminBossScheduleNetMsg
            {
                NpcId = BossNpcId,
                Action = BossProtocol.ActionMarkDead
            }),
            timeout.Token);
        Assert.Equal(Fineage.Protocol.InventoryProtocol.Forbidden, code);
    }

    [Fact]
    public void Status_codec_round_trips_every_field()
    {
        var message = new BossStatusNetMsg
        {
            Bosses =
            [
                new BossStatusEntryNet
                {
                    NpcId = BossNpcId,
                    ObjectId = BossObjectId,
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

        var copy = NetCodec.DecodeBossStatus(NetCodec.EncodeBossStatus(message));
        var row = Assert.Single(copy.Bosses);
        Assert.Equal(BossNpcId, row.NpcId);
        Assert.Equal(BossObjectId, row.ObjectId);
        Assert.Equal(BossProtocol.KindGrand, row.Kind);
        Assert.Equal(BossProtocol.StatusDead, row.Status);
        Assert.Equal(50, row.RespawnUnixMs);
        Assert.Equal(9, row.CurrentHp);
        Assert.Equal(10, row.MaxHp);
        Assert.Equal(3, row.CurrentMp);
        Assert.Equal(4, row.MaxMp);
        Assert.False(row.Enabled);
        Assert.True(row.Spawned);
    }

    const int BossNpcId = 25001;
    const int BossObjectId = 80001;
    const int BossX = -54416, BossY = 146480, BossZ = -2887;

    static BossDefinition Boss(bool enabled)
    {
        var (min, max) = BossCatalog.RaidWindow(1200, 600);
        return new BossDefinition(
            BossNpcId,
            BossKind.Raid,
            enabled,
            BossObjectId,
            "Greyclaw Kutus",
            "Raid Boss",
            "RaidBoss",
            BossX,
            BossY,
            BossZ,
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
}
