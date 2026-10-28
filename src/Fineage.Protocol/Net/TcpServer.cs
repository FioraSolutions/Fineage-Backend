using System.Net;
using System.Net.Sockets;
using Fineage.Protocol.Telemetry;

namespace Fineage.Protocol.Net;

public sealed class TcpServer
{
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
            client.LingerState = new LingerOption(true, 0);
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
            client.Dispose();
            slots.Release();
            FineageTelemetry.Connections.Add(-1, Tag(endpointName));
        }
    }

    static KeyValuePair<string, object?> Tag(string endpoint) => new("endpoint", endpoint);
}
