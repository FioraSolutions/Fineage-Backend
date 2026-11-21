using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;
using static Fineage.Zone.Tests.NetMoveVisibilityRig;

namespace Fineage.Zone.Tests;

/// <summary>
/// What other players see of a moving hero, over real Net sockets (2–3 sessions on one zone). Mobius parity:
/// MoveToLocation to the knownlist on every new leg, StopMove broadcast, CharInfo + MoveToLocation / ChangeWaitType
/// when a pawn enters someone's view, DeleteObject when it leaves; the server never trusts client coordinates.
/// </summary>
public sealed class NetMoveVisibilityTests
{
    static CancellationTokenSource Timeout(int seconds = 30) => new(TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task Peer_in_view_gets_the_run_start_and_the_stop_lands_on_the_server_position_without_extra_frames()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        var c = await rig.JoinAsync(X0 + 5000, Y0);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync(), c.DrainAsync());
        float run = a.Entity.RunSpeed;
        Assert.True(a.Entity.Running);

        await a.MoveAsync(X0 + 1000, Y0);
        var start = await b.NextMoveOfAsync(a);
        Assert.Equal((X0, Y0, Z0), (start.OriginX, start.OriginY, start.OriginZ));
        Assert.Equal((X0 + 1000, Y0, Z0), (start.DestX, start.DestY, start.DestZ));
        Assert.True(start.Running);
        var selfStart = await a.NextMoveOfAsync(a);
        Assert.Equal(X0 + 1000, selfStart.DestX);

        await rig.AdvanceAsync(1000);
        int expectX = X0 + (int)Math.Round(run);
        Assert.InRange(a.Entity.X, expectX - 2, expectX + 2);

        await a.StopAsync();
        var correction = await a.NextCorrectionAsync();
        var stop = await b.NextMoveOfAsync(a);
        var e = a.Entity;
        Assert.False(e.MoveActive);
        Assert.Equal((e.X, e.Y, e.Z), (correction.X, correction.Y, correction.Z));
        Assert.Equal((e.X, e.Y, e.Z), (stop.OriginX, stop.OriginY, stop.OriginZ));
        Assert.Equal((e.X, e.Y, e.Z), (stop.DestX, stop.DestY, stop.DestZ));

        // No storm: ten ticks of running produced no per-tick move spam; an idle StopMove fans nothing.
        await a.StopAsync();
        await a.NextCorrectionAsync();
        var bTail = About(await b.DrainAsync(), a);
        Assert.Empty(bTail);
        Assert.Empty(About(await c.DrainAsync(), a));
    }

    [Fact]
    public async Task A_new_destination_mid_run_retargets_peers_from_where_the_runner_really_is()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync());

        await a.MoveAsync(X0 + 1500, Y0);
        await b.NextMoveOfAsync(a);
        await rig.AdvanceAsync(400);
        int midX = a.Entity.X;
        Assert.True(midX > X0 + 20);

        await a.MoveAsync(X0, Y0 + 800);
        var turn = await b.NextMoveOfAsync(a);
        Assert.Equal((midX, Y0), (turn.OriginX, turn.OriginY));
        Assert.Equal((X0, Y0 + 800), (turn.DestX, turn.DestY));

        // Arrival is extrapolated by the client: no extra frame until the next order.
        await rig.AdvanceAsync(10_000, 250);
        Assert.Equal((X0, Y0 + 800), (a.Entity.X, a.Entity.Y));
        Assert.DoesNotContain(About(await b.DrainAsync(), a), f => f.Opcode == NetOpcode.MoveState);
    }

    [Fact]
    public async Task Walk_toggle_mid_run_reaches_peers_as_gait_and_a_walk_speed_leg()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync());
        float walk = a.Entity.WalkSpeed;
        Assert.True(walk > 0 && walk < a.Entity.RunSpeed);

        await a.MoveAsync(X0 + 1500, Y0);
        await b.NextMoveOfAsync(a);
        await rig.AdvanceAsync(400);

        await a.ActionAsync(ActionProtocol.WalkRun);
        var gait = NetCodec.DecodeChangeMoveType((await b.Net.ExpectAsync(NetOpcode.ChangeMoveType, timeout.Token)).Payload);
        Assert.Equal(a.EntityValue, gait.EntityValue);
        Assert.False(gait.Running);
        Assert.Equal(walk, gait.WalkSpeed);
        var leg = await b.NextMoveOfAsync(a);
        Assert.False(leg.Running);
        int fromX = a.Entity.X;
        Assert.Equal(fromX, leg.OriginX);
        Assert.Equal(X0 + 1500, leg.DestX);

        await rig.AdvanceAsync(1000);
        Assert.InRange(a.Entity.X - fromX, (int)walk - 2, (int)walk + 2);
    }

    [Fact]
    public async Task Outsider_sees_nothing_then_the_runner_spawns_moving_on_entry_and_despawns_on_exit()
    {
        using var timeout = Timeout(60);
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var c = await rig.JoinAsync(X0 + 3000, Y0);
        Assert.Empty(About(a.Net.EnterFrames, c));
        Assert.Empty(About(c.Net.EnterFrames, a));
        await Task.WhenAll(a.DrainAsync(), c.DrainAsync());

        await a.MoveAsync(X0 + 2600, Y0);
        await a.NextMoveOfAsync(a);
        Assert.Empty(About(await c.DrainAsync(100), a));

        // ~125u/s: about 8 s to come within range; the AOI refresh runs every AoiRefreshDistance of travel.
        for (int i = 0; i < 200 && a.Entity.X < X0 + 1000 + ZoneWorld.AoiRefreshDistance; i++)
            await rig.AdvanceAsync(200, 200);
        var spawn = await c.NextSpawnOfAsync(a);
        Assert.False(spawn.IsSelf);
        Assert.InRange(spawn.X, X0 + 1000, X0 + 1000 + ZoneWorld.AoiRefreshDistance + 200);
        var moving = await c.NextMoveOfAsync(a);
        Assert.Equal((spawn.X, spawn.Y), (moving.OriginX, moving.OriginY));
        Assert.Equal((X0 + 2600, Y0), (moving.DestX, moving.DestY));
        await a.NextSpawnOfAsync(c);

        // Leave again: past range + forget slack, C drops the pawn.
        await rig.AdvanceAsync(10_000, 250);
        await a.MoveAsync(X0 - 1000, Y0);
        await a.NextMoveOfAsync(a);   // order processed before the clock moves
        await rig.AdvanceAsync(40_000, 250);
        await c.NextDespawnOfAsync(a);
        await a.NextDespawnOfAsync(c);
    }

    [Fact]
    public async Task Late_joiner_sees_a_runner_mid_leg_heading_to_its_live_destination()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        await a.DrainAsync();
        await a.MoveAsync(X0 + 1500, Y0);
        await a.NextMoveOfAsync(a);
        await rig.AdvanceAsync(400);
        int midX = a.Entity.X;

        var d = await rig.JoinAsync(X0 + 500, Y0 + 200);
        var aboutA = About(d.Net.EnterFrames, a);
        Assert.True(aboutA.Count >= 2, $"late joiner got {aboutA.Count} frames about the runner");
        Assert.Equal(NetOpcode.EntitySpawn, aboutA[0].Opcode);
        var spawn = NetCodec.DecodeEntitySpawn(aboutA[0].Payload);
        Assert.Equal(midX, spawn.X);
        Assert.Equal(NetOpcode.MoveState, aboutA[1].Opcode);
        var move = NetCodec.DecodeMoveState(aboutA[1].Payload);
        Assert.Equal((midX, Y0), (move.OriginX, move.OriginY));
        Assert.Equal((X0 + 1500, Y0), (move.DestX, move.DestY));
        Assert.True(move.Running);
    }

    [Fact]
    public async Task Seated_player_is_seen_seated_by_peers_late_joiners_and_walkers_by()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var b = await rig.JoinAsync(X0, Y0);
        var a = await rig.JoinAsync(X0 + 300, Y0);
        var walker = await rig.JoinAsync(X0 + 3500, Y0);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync(), walker.DrainAsync());

        await b.ActionAsync(ActionProtocol.SitStand);
        var sit = NetCodec.DecodeChangeWaitType((await a.Net.ExpectAsync(NetOpcode.ChangeWaitType, timeout.Token)).Payload);
        Assert.Equal(b.EntityValue, sit.EntityValue);
        Assert.Equal(ActionProtocol.WaitSitting, sit.WaitType);
        Assert.True(b.Entity.Running, "sitting keeps the gait (Mobius sitDown)");
        Assert.Empty(About(await walker.DrainAsync(), b));

        // Sitting blocks movement: the client is corrected and peers see no move.
        await b.MoveAsync(X0 + 500, Y0);
        var corr = await b.NextCorrectionAsync();
        Assert.Equal((X0, Y0), (corr.X, corr.Y));

        var late = await rig.JoinAsync(X0 - 300, Y0);
        var aboutB = About(late.Net.EnterFrames, b);
        Assert.Equal([NetOpcode.EntitySpawn, NetOpcode.ChangeWaitType], aboutB.Select(f => f.Opcode).ToArray());
        Assert.Equal(ActionProtocol.WaitSitting, NetCodec.DecodeChangeWaitType(aboutB[1].Payload).WaitType);

        await walker.MoveAsync(X0 + 500, Y0);
        await walker.NextMoveOfAsync(walker);
        await rig.AdvanceAsync(30_000, 250);
        await walker.NextSpawnOfAsync(b);
        var seen = NetCodec.DecodeChangeWaitType((await walker.Net.ExpectAsync(NetOpcode.ChangeWaitType, timeout.Token)).Payload);
        Assert.Equal(b.EntityValue, seen.EntityValue);
        Assert.Equal(ActionProtocol.WaitSitting, seen.WaitType);
        Assert.DoesNotContain(About(await a.DrainAsync(), b), f => f.Opcode == NetOpcode.MoveState);
    }

    [Fact]
    public async Task Teleport_despawns_the_jumper_for_old_peers_and_spawns_it_once_for_new_ones()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        // NetTestKit spawn is in elf_town; its village restart points cluster around (45400, 49550).
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        var c = await rig.JoinAsync(45400, 49550, -2950);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync(), c.DrainAsync());

        Assert.Null(await rig.World.TryAdminKillPlayerAsync(a.Entity.Session!, timeout.Token));
        await a.Net.SendAsync(NetOpcode.ReviveRequest,
            NetCodec.EncodeReviveRequest(new ReviveRequestNetMsg { Action = ReviveRequestNetMsg.ReviveToVillage }), timeout.Token);

        var spawned = await c.NextSpawnOfAsync(a);
        Assert.Equal((a.Entity.X, a.Entity.Y), (spawned.X, spawned.Y));
        await b.NextDespawnOfAsync(a);

        var bTail = About(await b.DrainAsync(400), a);
        var cTail = About(await c.DrainAsync(400), a);
        Assert.DoesNotContain(bTail, f => f.Opcode is NetOpcode.EntityDespawn or NetOpcode.EntitySpawn);
        Assert.DoesNotContain(cTail, f => f.Opcode is NetOpcode.EntityDespawn or NetOpcode.EntitySpawn);
    }

    [Fact]
    public async Task Disconnect_mid_run_despawns_once_for_peers_and_never_reaches_outsiders()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        var c = await rig.JoinAsync(X0 + 5000, Y0);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync(), c.DrainAsync());
        await a.MoveAsync(X0 + 1000, Y0);
        await b.NextMoveOfAsync(a);
        await rig.AdvanceAsync(300);

        await a.Net.DisposeAsync();
        await b.NextDespawnOfAsync(a);
        for (int i = 0; i < 200 && rig.World.OnlinePlayerCount != 2; i++)
            await Task.Delay(10, timeout.Token);
        Assert.Equal(2, rig.World.OnlinePlayerCount);
        await rig.AdvanceAsync(1000);

        Assert.Empty(About(await b.DrainAsync(400), a));
        Assert.Empty(About(await c.DrainAsync(100), a));
    }

    [Fact]
    public async Task Bogus_far_client_position_is_corrected_for_the_sender_and_never_shown_to_peers()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync());

        await a.ValidateAsync(X0 + 6000, Y0 + 6000);
        var corr = await a.NextCorrectionAsync();
        Assert.Equal((X0, Y0, Z0), (corr.X, corr.Y, corr.Z));

        // A move order past the per-request clamp is refused the same way.
        await a.MoveAsync(X0 + 90_000, Y0);
        corr = await a.NextCorrectionAsync();
        Assert.Equal((X0, Y0), (corr.X, corr.Y));

        Assert.Equal((X0, Y0), (a.Entity.X, a.Entity.Y));
        Assert.Empty(About(await b.DrainAsync(400), a));
    }
}
