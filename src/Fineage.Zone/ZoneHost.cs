using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Net.Host;
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
        var created = world ?? ZoneAoiSettings.CreateWorld(accounts: accounts);
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

    /// <summary>
    /// Temporary bridge for the tests and <see cref="ZoneMultiplayerAcceptance"/> that still speak the retired
    /// V2 wire. Delete once the remaining suites move to Net.
    /// </summary>
    public static ZoneHost Start(
        HandoffTokens tokens,
        ZoneWorld? world = null,
        Func<DateTimeOffset>? clock = null,
        ZoneServerOptions? options = null) => StartV2(tokens, world, clock, options);

    /// <summary>
    /// Temporary bridge for the tests that still speak the retired V2 wire.
    /// Delete with them once the suites move to Net.
    /// </summary>
    public static ZoneHost StartV2(HandoffTokens tokens, ZoneWorld? world = null, Func<DateTimeOffset>? clock = null, ZoneServerOptions? options = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cts = new CancellationTokenSource();
        var created = world ?? ZoneAoiSettings.CreateWorld();
        var server = new ZoneServer(tokens, created, clock, options);
        created.StartSimulation(cts.Token);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(cts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                    {
                        break;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await using var stream = client.GetStream();
                            await server.HandleAsync(stream, cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                        {
                        }
                        finally
                        {
                            client.Dispose();
                        }
                    });
                }
            }
            catch (ObjectDisposedException)
            {
            }
        });

        return new ZoneHost(cts, port, listener.Stop);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _stop();
        _cts.Dispose();
    }
}
