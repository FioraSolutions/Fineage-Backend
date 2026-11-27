using System.Net;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class NetFanoutStripesTests
{
    [Fact]
    public async Task Connection_id_assigns_stripe_by_modulo()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var a = new TcpClient();
        await a.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var serverA = listener.AcceptTcpClient();
        using var b = new TcpClient();
        await b.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var serverB = listener.AcceptTcpClient();

        await using var connA = new NetConnection(serverA, 4);
        await using var connB = new NetConnection(serverB, 4);
        Assert.NotEqual(connA.ConnectionId, connB.ConnectionId);

        await using var stripes = new NetFanoutStripes(stripeCount: 4);
        Assert.Equal(4, stripes.StripeCount);
        Assert.InRange((int)(connA.ConnectionId % 4), 0, 3);
        Assert.InRange((int)(connB.ConnectionId % 4), 0, 3);
    }

    [Fact]
    public async Task Reliable_admission_disconnects_only_the_recipient_over_the_work_budget()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var a = new TcpClient();
        await a.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var serverA = listener.AcceptTcpClient();
        using var b = new TcpClient();
        await b.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var serverB = listener.AcceptTcpClient();
        await using var connA = new NetConnection(serverA, 4, endpoint: "fanout-admission");
        await using var connB = new NetConnection(serverB, 4, endpoint: "fanout-admission");
        connA.BindPresence(1, 1);
        connB.BindPresence(2, 1);
        var handleA = new ConnectionHandle(1, 1, connA);
        var handleB = new ConnectionHandle(2, 1, connB);
        await using var stripes = new NetFanoutStripes(stripeCount: 4);

        var admission = new FanoutReliableAdmission((ids, opcode) => new AudienceSnapshot(
            ids.Select(id => id == 1 ? handleA : handleB).ToArray(), 0));
        long charged = admission.Admit([1, 2], NetOpcode.CreatureSay, remainingWork: 1, limit: 10, "test");
        Assert.Equal(1, charged);
        // A recipient already marked for disconnect does not queue further frames.
        stripes.EnqueueSnapshot(handleB, [new OutboundFrame(NetOpcode.CreatureSay, [3])]);

        Assert.True(connA.TrySend(NetOpcode.CreatureSay, [4]));
        Assert.False(connB.IsBoundTo(handleB));
        Assert.True(a.Connected);
    }
}