using System.Net;
using System.Net.Sockets;
using Fineage.Protocol.Telemetry;

namespace Fineage.Protocol.Net;

public sealed class TcpServer
{
    /// <summary>Upper bound on how long a closing connection is drained before the final close.</summary>
    static readonly TimeSpan CloseDrainTimeout = TimeSpan.FromMilliseconds(250);

    int _nextId = 1;

    public int BoundPort { get; private set; }

    public async Task RunAsync(
        string host,
        int port,
        int maxConnections,
        int outboundQueue,
        string endpointName,
        Func<PipelineConnection, CancellationToken, Task> handler,
        CancellationToken ct,
        Func<Stream, ValueTask>? reject = null)
    {
        IPAddress address = host is "0.0.0.0" or "*" or ""
            ? IPAddress.Any
            : IPAddress.Parse(host);

        var listener = new TcpListener(address, port);
        listener.Start();
        BoundPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Not disposed: ServeAsync tasks may Release after the accept loop exits (test host crashed on
        // ObjectDisposedException). SemaphoreSlim without AvailableWaitHandle holds no unmanaged state.
        var slots = new SemaphoreSlim(Math.Max(1, maxConnections));
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
                    FineageTelemetry.ConnectionsRejected.Add(1, Tag(endpointName));
                    if (reject != null)
                    {
                        try
                        {
                            await reject(client.GetStream()).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
                        {
                        }
                    }

                    client.Dispose();
                    continue;
                }

                FineageTelemetry.Connections.Add(1, Tag(endpointName));
                int id = Interlocked.Increment(ref _nextId);
                _ = Task.Run(() => ServeAsync(client, id, slots, outboundQueue, endpointName, handler, ct), ct);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task ServeAsync(
        TcpClient client,
        int id,
        SemaphoreSlim slots,
        int outboundQueue,
        string endpointName,
        Func<PipelineConnection, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        try
        {
            client.NoDelay = true;
            string remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
            await using var connection = new PipelineConnection(
                id, remote, client.GetStream(), outboundQueue, endpointName, ct);
            await handler(connection, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException or ObjectDisposedException)
        {
        }
        finally
        {
            // Free the slot before the drain: a rate-limit kick must not keep capacity for a peer that
            // still has bytes in flight.
            slots.Release();
            FineageTelemetry.Connections.Add(-1, Tag(endpointName));
            await CloseGracefullyAsync(client).ConfigureAwait(false);
            client.Dispose();
        }
    }

    /// <summary>
    /// Half-closes the socket and drains what the peer already sent, so the peer observes EOF instead of
    /// an RST. Two things turn a normal hangup into an abortive close: SO_LINGER with a zero timeout
    /// (always RST) and closing a socket that still has unread bytes queued (RST on the leftover).
    /// Neither is what "the session is over" should look like on the wire.
    /// </summary>
    static async Task CloseGracefullyAsync(TcpClient client)
    {
        try
        {
            Socket socket = client.Client;
            socket.Shutdown(SocketShutdown.Send);

            using var drain = new CancellationTokenSource(CloseDrainTimeout);
            byte[] scratch = new byte[1024];
            while (socket.Poll(0, SelectMode.SelectRead))
            {
                int n = await socket.ReceiveAsync(scratch, SocketFlags.None, drain.Token).ConfigureAwait(false);
                if (n == 0)
                    return;
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    static KeyValuePair<string, object?> Tag(string endpoint) => new("endpoint", endpoint);
}
