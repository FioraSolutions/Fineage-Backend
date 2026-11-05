using System.Net;
using System.Net.Sockets;

namespace Fineage.Net.Host;

public sealed class NetTcpServer
{
    public int BoundPort { get; private set; }

    /// <summary>Telemetry <c>endpoint</c> tag for this listener (<c>login</c> or <c>zone</c>).</summary>
    public string Endpoint { get; init; } = "net";

    public async Task RunAsync(
        string host,
        int port,
        Func<NetConnection, CancellationToken, Task> handler,
        CancellationToken ct,
        int maxConnections = 128,
        int inboxCapacity = 256,
        int outboundCapacity = NetConnection.DefaultOutboundCapacity)
    {
        IPAddress address = host is "0.0.0.0" or "*" or ""
            ? IPAddress.Any
            : IPAddress.Parse(host);

        var listener = new TcpListener(address, port);
        listener.Start();
        BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var slots = new SemaphoreSlim(Math.Max(1, maxConnections));
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!slots.Wait(0))
                {
                    NetTelemetry.ConnectionRejected(Endpoint);
                    client.Dispose();
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    NetTelemetry.ConnectionOpened(Endpoint);
                    try
                    {
                        await using var conn = new NetConnection(client, inboxCapacity, Endpoint, outboundCapacity);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        var read = conn.RunReadLoopAsync(linked.Token);
                        try
                        {
                            await handler(conn, linked.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            try { linked.Cancel(); } catch (ObjectDisposedException) { /* ignore */ }
                            try { await read.ConfigureAwait(false); } catch { /* ignore */ }
                        }
                    }
                    catch
                    {
                        /* connection-level failures must not take down the accept loop */
                    }
                    finally
                    {
                        NetTelemetry.ConnectionClosed(Endpoint);
                        try { slots.Release(); } catch (ObjectDisposedException) { /* accept loop ended */ }
                    }
                }, CancellationToken.None);
            }
        }
        finally
        {
            listener.Stop();
        }
    }
}
