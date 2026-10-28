using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Zone.World;

namespace Fineage.Zone;

sealed class ZoneHost : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts;
    readonly Task _loop;

    ZoneHost(TcpListener listener, CancellationTokenSource cts, Task loop)
    {
        _listener = listener;
        _cts = cts;
        _loop = loop;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public static ZoneHost Start(HandoffTokens tokens, ZoneWorld? world = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var cts = new CancellationTokenSource();
        var server = world == null ? new ZoneServer(tokens) : new ZoneServer(tokens, world);
        var loop = Task.Run(async () =>
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

        return new ZoneHost(listener, cts, loop);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        _cts.Dispose();
    }
}
