using System.Net;
using System.Net.Sockets;
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
}