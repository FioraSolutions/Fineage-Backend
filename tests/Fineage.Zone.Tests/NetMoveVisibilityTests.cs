using Fineage.Net.Contract;
using Fineage.Protocol;
using Xunit;
using static Fineage.Zone.Tests.NetMoveVisibilityRig;

namespace Fineage.Zone.Tests;

/// <summary>
/// Real loopback Net smoke for handshake, enter, move, and delivery. Functional AOI/move rules live in
/// <see cref="NetMoveVisibilityMailboxTests"/> with stepped simulation and the recorder mailbox.
/// </summary>
[Collection(nameof(NetMoveVisibilityCollection))]
[Trait("Category", "Integration")]
public sealed class NetMoveVisibilityTests
{
    static CancellationTokenSource Timeout(int seconds = 30) => new(TimeSpan.FromSeconds(seconds));

    [Fact]
    public async Task Tcp_handshake_enter_move_and_peer_delivery()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        int bMoveBefore = b.Net.EnterFrames.Count;
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync());

        await a.MoveAsync(X0 + 1000, Y0);
        var start = await b.NextMoveOfAsync(a);
        Assert.Equal((X0, Y0, Z0), (start.OriginX, start.OriginY, start.OriginZ));
        Assert.Equal((X0 + 1000, Y0, Z0), (start.DestX, start.DestY, start.DestZ));
        Assert.True(start.Running);

        rig.StepTicks(5);
        await a.StopAsync();
        _ = await a.NextCorrectionAsync();
        var stop = await b.NextMoveOfAsync(a);
        var e = a.Entity;
        Assert.False(e.MoveActive);
        Assert.Equal((e.X, e.Y, e.Z), (stop.OriginX, stop.OriginY, stop.OriginZ));

        int after = b.Net.EnterFrames.Count;
        Assert.Equal(bMoveBefore, after);
    }

    [Fact]
    public async Task Tcp_bogus_position_corrected_on_sender_only()
    {
        using var timeout = Timeout();
        await using var rig = await StartAsync(timeout.Token);
        var a = await rig.JoinAsync(X0, Y0);
        var b = await rig.JoinAsync(X0 + 300, Y0);
        await Task.WhenAll(a.DrainAsync(), b.DrainAsync());
        int peerMoves = About(await b.DrainAsync(), a).Count;

        await a.ValidateAsync(X0 + 6000, Y0 + 6000);
        var corr = await a.NextCorrectionAsync();
        Assert.Equal((X0, Y0, Z0), (corr.X, corr.Y, corr.Z));
        rig.StepTicks(3);
        Assert.Equal(peerMoves, About(b.Net.EnterFrames, a).Count(f => f.Opcode == NetOpcode.MoveState));
    }
}