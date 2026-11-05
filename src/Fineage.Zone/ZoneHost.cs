using System.Net;
using Fineage.Login;
using Fineage.Net.Host;
using Fineage.Zone.Net;
using Fineage.Zone.World;

namespace Fineage.Zone;

/// <summary>
/// Loopback zone listener for the selftest and tests: starts a <see cref="NetZoneServer"/> on an ephemeral
/// port and the world simulation beside it. Net (PacketEnvelope) is the only zone transport.
/// </summary>
sealed class ZoneHost : IAsyncDisposable
{
    readonly NetTcpServer _server;
    readonly CancellationTokenSource _cts;

    ZoneHost(NetTcpServer server, CancellationTokenSource cts, int port)
    {
        _server = server;
        _cts = cts;
        Port = port;
    }

    public int Port { get; }

    public static ZoneHost Start(HandoffTokens tokens, ZoneWorld? world = null, IAccountStore? accounts = null)
    {
        var cts = new CancellationTokenSource();
        var created = world ?? ZoneAoiSettings.CreateWorld(accounts: accounts);
        created.StartSimulation(cts.Token);
        var netZone = new NetZoneServer(tokens, created, accounts);
        var server = new NetTcpServer();
        _ = server.RunAsync("127.0.0.1", 0, netZone.HandleAsync, cts.Token, maxConnections: 16, inboxCapacity: 64);

        int port = 0;
        for (int i = 0; i < 200 && port == 0; i++)
        {
            Thread.Sleep(10);
            port = server.BoundPort;
        }

        if (port == 0)
            throw new InvalidOperationException("zone net server did not bind a port");

        return new ZoneHost(server, cts, port);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
