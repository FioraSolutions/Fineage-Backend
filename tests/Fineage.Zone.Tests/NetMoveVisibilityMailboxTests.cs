using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Move visibility without loopback sockets: commands, stepped simulation, recorder assertions.</summary>
[Trait("Category", "Integration")]
public sealed class NetMoveVisibilityMailboxTests
{
    static int Count(NetBridgeRecorder rec) => rec.Calls.Count;

    [Fact]
    public async Task Peer_in_view_gets_run_start_and_stop_at_server_position()
    {
        var m = new MoveVisibilityMailbox();
        var a = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var b = await m.EnterAsync(MoveVisibilityMailbox.X0 + 300, MoveVisibilityMailbox.Y0);
        var c = await m.EnterAsync(MoveVisibilityMailbox.X0 + 5000, MoveVisibilityMailbox.Y0);
        float run = a.Player.RunSpeed;
        Assert.True(a.Player.Running);

        Assert.Null(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0 + 1000, MoveVisibilityMailbox.Y0, MoveVisibilityMailbox.Z0,
            CancellationToken.None));
        var peerMoves = MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId);
        Assert.NotEmpty(peerMoves);
        var (ox, oy, dx, dy, running) = MoveVisibilityMailbox.DecodeMove(peerMoves[0]);
        Assert.Equal((MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0), (ox, oy));
        Assert.Equal((MoveVisibilityMailbox.X0 + 1000, MoveVisibilityMailbox.Y0), (dx, dy));
        Assert.True(running);

        m.Step(5);
        int expectX = MoveVisibilityMailbox.X0 + (int)Math.Round(run);
        Assert.InRange(a.Player.X, expectX - 2, expectX + 2);

        m.Bench.World.StopMove(a.Session);
        m.Step(1);
        Assert.False(a.Player.MoveActive);
        var stopMoves = MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Skip(peerMoves.Count);
        Assert.NotEmpty(stopMoves);
        var stop = MoveVisibilityMailbox.DecodeMove(stopMoves.Last());
        Assert.Equal((a.Player.X, a.Player.Y), (stop.originX, stop.originY));
        Assert.Equal((a.Player.X, a.Player.Y), (stop.destX, stop.destY));

        int peerMoveCount = MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Count;
        int outsiderMoveCount = MoveVisibilityMailbox.MovesFor(m.Rec, c.ObjectId, a.ObjectId).Count;
        m.Bench.World.StopMove(a.Session);
        m.Step(1);
        Assert.Equal(peerMoveCount, MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Count);
        Assert.Equal(outsiderMoveCount, MoveVisibilityMailbox.MovesFor(m.Rec, c.ObjectId, a.ObjectId).Count);
    }

    [Fact]
    public async Task Mid_run_retarget_starts_from_live_position()
    {
        var m = new MoveVisibilityMailbox();
        var a = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var b = await m.EnterAsync(MoveVisibilityMailbox.X0 + 300, MoveVisibilityMailbox.Y0);

        Assert.Null(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0 + 1500, MoveVisibilityMailbox.Y0,
            MoveVisibilityMailbox.Z0, CancellationToken.None));
        int firstMoves = MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Count;
        m.Step(2);
        int midX = a.Player.X;
        Assert.True(midX > MoveVisibilityMailbox.X0 + 20);

        Assert.Null(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0 + 800,
            MoveVisibilityMailbox.Z0, CancellationToken.None));
        var turn = MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Skip(firstMoves).First();
        var leg = MoveVisibilityMailbox.DecodeMove(turn);
        Assert.Equal((midX, MoveVisibilityMailbox.Y0), (leg.originX, leg.originY));
        Assert.Equal((MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0 + 800), (leg.destX, leg.destY));

        m.AdvanceMs(10_000);
        Assert.Equal((MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0 + 800), (a.Player.X, a.Player.Y));
        int after = Count(m.Rec);
        Assert.Empty(MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Skip(after));
    }

    [Fact]
    public async Task Walk_toggle_mid_run_reaches_peer_as_walk_leg()
    {
        var m = new MoveVisibilityMailbox();
        var a = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var b = await m.EnterAsync(MoveVisibilityMailbox.X0 + 300, MoveVisibilityMailbox.Y0);
        float walk = a.Player.WalkSpeed;
        Assert.True(walk > 0 && walk < a.Player.RunSpeed);

        Assert.Null(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0 + 1500, MoveVisibilityMailbox.Y0,
            MoveVisibilityMailbox.Z0, CancellationToken.None));
        int moves0 = MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Count;
        m.Step(2);

        Assert.Null(await m.Bench.World.TryCharacterActionAsync(a.Session, ActionProtocol.WalkRun, 0, CancellationToken.None));
        m.Step(1);
        var gait = m.Rec.Of(nameof(IWorldEventPublisher.FanoutChangeMoveType))
            .Last(a => ((IReadOnlyList<int>)a[0]!).Contains(b.ObjectId));
        Assert.False((bool)gait[2]!);
        Assert.Equal(walk, (float)gait[4]!);
        var leg = MoveVisibilityMailbox.DecodeMove(MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Skip(moves0).Last());
        Assert.False(leg.running);
        int fromX = a.Player.X;
        Assert.InRange(leg.originX, fromX - 20, fromX + 4);
        Assert.Equal(MoveVisibilityMailbox.X0 + 1500, leg.destX);

        m.Step(5);
        Assert.InRange(a.Player.X - fromX, (int)walk - 2, (int)walk + 2);
    }

    [Fact]
    public async Task Outsider_sees_spawn_on_entry_and_despawn_on_exit()
    {
        var m = new MoveVisibilityMailbox();
        var a = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var c = await m.EnterAsync(MoveVisibilityMailbox.X0 + 3000, MoveVisibilityMailbox.Y0);
        Assert.DoesNotContain(a.ObjectId, c.Player.Knownlist);

        Assert.Null(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0 + 2600, MoveVisibilityMailbox.Y0,
            MoveVisibilityMailbox.Z0, CancellationToken.None));
        int targetX = MoveVisibilityMailbox.X0 + 1000 + ZoneWorld.AoiRefreshDistance;
        for (int i = 0; i < 120 && a.Player.X < targetX; i++)
            m.Step(1);
        Assert.True(a.Player.X >= targetX - ZoneWorld.AoiRefreshDistance, $"runner x={a.Player.X}");

        for (int i = 0; i < 150 && !c.Player.Knownlist.Contains(a.ObjectId); i++)
            m.Step(1);
        Assert.Contains(a.ObjectId, c.Player.Knownlist);

        m.AdvanceMs(10_000);
        Assert.Null(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0 - 1000, MoveVisibilityMailbox.Y0,
            MoveVisibilityMailbox.Z0, CancellationToken.None));
        m.Step(1);
        for (int i = 0; i < 220 && c.Player.Knownlist.Contains(a.ObjectId); i++)
            m.Step(5);
        Assert.DoesNotContain(a.ObjectId, c.Player.Knownlist);
    }

    [Fact]
    public async Task Grid_mover_entering_stationary_observer_aoi_spawns_each_for_the_other()
    {
        var m = new MoveVisibilityMailbox(new GridAoi(1000), GeoEngine.Empty);
        var observer = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var mover = await m.EnterAsync(MoveVisibilityMailbox.X0 + 5000, MoveVisibilityMailbox.Y0);
        Assert.DoesNotContain(mover.ObjectId, observer.Player.Knownlist);
        Assert.DoesNotContain(observer.ObjectId, mover.Player.Knownlist);

        Assert.Null(await m.Bench.World.TryMoveAsync(mover.Session, MoveVisibilityMailbox.X0 + 1500,
            MoveVisibilityMailbox.Y0, MoveVisibilityMailbox.Z0, CancellationToken.None));
        for (int i = 0; i < 600 && !observer.Player.Knownlist.Contains(mover.ObjectId); i++)
            m.Step(1);

        // The recorder sees one diff for the mover; the observer's EntitySpawn is the reciprocal half of that same drain.
        Assert.NotEmpty(MoveVisibilityMailbox.KnownAdds(m.Rec, mover.ObjectId, observer.ObjectId));
        Assert.Empty(MoveVisibilityMailbox.KnownRemoves(m.Rec, mover.ObjectId, observer.ObjectId));
        Assert.Contains(mover.ObjectId, observer.Player.Knownlist);
        Assert.Contains(observer.ObjectId, mover.Player.Knownlist);
    }

    [Fact]
    public async Task Grid_mover_entering_observer_aoi_from_negative_cell_spawns_each_for_the_other()
    {
        var m = new MoveVisibilityMailbox(new GridAoi(1000), GeoEngine.Empty);
        var observer = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var mover = await m.EnterAsync(MoveVisibilityMailbox.X0 - 5000, MoveVisibilityMailbox.Y0);
        Assert.DoesNotContain(mover.ObjectId, observer.Player.Knownlist);
        Assert.DoesNotContain(observer.ObjectId, mover.Player.Knownlist);

        Assert.Null(await m.Bench.World.TryMoveAsync(mover.Session, MoveVisibilityMailbox.X0 - 500,
            MoveVisibilityMailbox.Y0, MoveVisibilityMailbox.Z0, CancellationToken.None));
        for (int i = 0; i < 600 && !observer.Player.Knownlist.Contains(mover.ObjectId); i++)
            m.Step(1);

        Assert.NotEmpty(MoveVisibilityMailbox.KnownAdds(m.Rec, mover.ObjectId, observer.ObjectId));
        Assert.Empty(MoveVisibilityMailbox.KnownRemoves(m.Rec, mover.ObjectId, observer.ObjectId));
        Assert.Contains(mover.ObjectId, observer.Player.Knownlist);
        Assert.Contains(observer.ObjectId, mover.Player.Knownlist);
    }

    [Fact]
    public async Task Grid_mover_leaving_and_reentering_observer_aoi_despawns_and_respawns_each_for_the_other()
    {
        var m = new MoveVisibilityMailbox(new GridAoi(1000), GeoEngine.Empty);
        var observer = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var mover = await m.EnterAsync(MoveVisibilityMailbox.X0 + 5000, MoveVisibilityMailbox.Y0);

        Assert.Null(await m.Bench.World.TryMoveAsync(mover.Session, MoveVisibilityMailbox.X0 + 1500,
            MoveVisibilityMailbox.Y0, MoveVisibilityMailbox.Z0, CancellationToken.None));
        for (int i = 0; i < 600 && !observer.Player.Knownlist.Contains(mover.ObjectId); i++)
            m.Step(1);
        int addsBeforeLeave = MoveVisibilityMailbox.KnownAdds(m.Rec, mover.ObjectId, observer.ObjectId).Count;
        Assert.Contains(mover.ObjectId, observer.Player.Knownlist);

        Assert.Null(await m.Bench.World.TryMoveAsync(mover.Session, MoveVisibilityMailbox.X0 + 8000,
            MoveVisibilityMailbox.Y0, MoveVisibilityMailbox.Z0, CancellationToken.None));
        for (int i = 0; i < 800 && observer.Player.Knownlist.Contains(mover.ObjectId); i++)
            m.Step(1);

        Assert.DoesNotContain(mover.ObjectId, observer.Player.Knownlist);
        Assert.DoesNotContain(observer.ObjectId, mover.Player.Knownlist);
        Assert.NotEmpty(MoveVisibilityMailbox.KnownRemoves(m.Rec, mover.ObjectId, observer.ObjectId));

        Assert.Null(await m.Bench.World.TryMoveAsync(mover.Session, MoveVisibilityMailbox.X0 + 1500,
            MoveVisibilityMailbox.Y0, MoveVisibilityMailbox.Z0, CancellationToken.None));
        for (int i = 0; i < 1200 && !observer.Player.Knownlist.Contains(mover.ObjectId); i++)
            m.Step(1);
        // Re-enter after the leave: the diff must spawn both sides again, not just restore the local set.

        Assert.Contains(mover.ObjectId, observer.Player.Knownlist);
        Assert.Contains(observer.ObjectId, mover.Player.Knownlist);
        Assert.True(MoveVisibilityMailbox.KnownAdds(m.Rec, mover.ObjectId, observer.ObjectId).Count > addsBeforeLeave);
    }

    [Fact]
    public async Task Late_joiner_sees_mid_leg_destination()
    {
        var m = new MoveVisibilityMailbox();
        var a = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        Assert.Null(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0 + 1500, MoveVisibilityMailbox.Y0,
            MoveVisibilityMailbox.Z0, CancellationToken.None));
        m.Step(2);
        int midX = a.Player.X;

        var d = await m.EnterAsync(MoveVisibilityMailbox.X0 + 500, MoveVisibilityMailbox.Y0 + 200);
        Assert.Contains(a.ObjectId, d.Player.Knownlist);
        Assert.True(a.Player.MoveActive);
        Assert.InRange(a.Player.X, midX - 4, midX + 4);
        Assert.Equal((MoveVisibilityMailbox.X0 + 1500, MoveVisibilityMailbox.Y0), (a.Player.MoveDestX, a.Player.MoveDestY));
        Assert.True(a.Player.Running);
    }

    [Fact]
    public async Task Bogus_far_client_position_never_shown_to_peers()
    {
        var m = new MoveVisibilityMailbox();
        var a = await m.EnterAsync(MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0);
        var b = await m.EnterAsync(MoveVisibilityMailbox.X0 + 300, MoveVisibilityMailbox.Y0);

        Assert.True(m.Bench.World.ValidateClientPosition(a.Session,
            MoveVisibilityMailbox.X0 + 6000, MoveVisibilityMailbox.Y0 + 6000, MoveVisibilityMailbox.Z0, out _, out _));
        Assert.Equal((MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0), (a.Player.X, a.Player.Y));

        int peerMoves = MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Count;
        Assert.NotNull(await m.Bench.World.TryMoveAsync(a.Session, MoveVisibilityMailbox.X0 + 90_000, MoveVisibilityMailbox.Y0,
            MoveVisibilityMailbox.Z0, CancellationToken.None));
        Assert.Equal((MoveVisibilityMailbox.X0, MoveVisibilityMailbox.Y0), (a.Player.X, a.Player.Y));
        Assert.Equal(peerMoves, MoveVisibilityMailbox.MovesFor(m.Rec, b.ObjectId, a.ObjectId).Count);
    }
}