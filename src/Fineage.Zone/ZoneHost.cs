using Fineage.Login;
using Fineage.Net.Host;
using Fineage.Zone.Interest;
using Fineage.Zone.Net;
using Fineage.Zone.World;

namespace Fineage.Zone;

/// <summary>
/// Loopback zone listener for the selftest and tests on the Net (PacketEnvelope) transport.
/// </summary>
sealed class ZoneHost : IAsyncDisposable
{
    readonly CancellationTokenSource _cts;
    readonly Action _stop;

    ZoneHost(CancellationTokenSource cts, int port, Action stop)
    {
        _cts = cts;
        Port = port;
        _stop = stop;
    }

    public int Port { get; }

    /// <summary>Net listener on an ephemeral port, with the world simulation started beside it.</summary>
    public static ZoneHost StartNet(HandoffTokens tokens, ZoneWorld? world = null, IAccountStore? accounts = null)
    {
        var cts = new CancellationTokenSource();
        // Loopback selftest: Cedric smoke map + whole-zone AOI. Program hosts the full datapack with GridAoi.
        var created = world ?? ZoneAoiSettings.CreateWorld(
            aoi: new SingleCellAoi(),
            accounts: accounts,
            includeEnabledMaps: false);
        created.StartSimulation(cts.Token);
        var server = new NetTcpServer();
        var netZone = new NetZoneServer(tokens, created, accounts);
        _ = server.RunAsync("127.0.0.1", 0, netZone.HandleAsync, cts.Token, maxConnections: 16, inboxCapacity: 64);

        int port = 0;
        for (int i = 0; i < 200 && port == 0; i++)
        {
            Thread.Sleep(10);
            port = server.BoundPort;
        }

        if (port == 0)
            throw new InvalidOperationException("zone net server did not bind a port");

        return new ZoneHost(cts, port, () => { });
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _stop();
        _cts.Dispose();
    }
}
