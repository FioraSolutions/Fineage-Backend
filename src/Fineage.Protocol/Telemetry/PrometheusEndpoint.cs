using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fineage.Protocol.Telemetry;

public sealed class PrometheusEndpoint : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts;
    readonly Task _loop;

    PrometheusEndpoint(TcpListener listener, CancellationTokenSource cts, Task loop, int port)
    {
        _listener = listener;
        _cts = cts;
        _loop = loop;
        Port = port;
    }

    public int Port { get; }

    public static PrometheusEndpoint Start(IPAddress address, int port, Func<string> scrape)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        int bound = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cts = new CancellationTokenSource();
        var loop = Task.Run(() => AcceptAsync(listener, scrape, cts.Token));
        return new PrometheusEndpoint(listener, cts, loop, bound);
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

    static async Task AcceptAsync(TcpListener listener, Func<string> scrape, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => RespondAsync(client, scrape, ct), ct);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    static async Task RespondAsync(TcpClient client, Func<string> scrape, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            client.NoDelay = true;
            await using NetworkStream stream = client.GetStream();
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(2));
            var buffer = new byte[2048];
            int read = await stream.ReadAsync(buffer, readTimeout.Token).ConfigureAwait(false);
            string head = Encoding.ASCII.GetString(buffer, 0, Math.Max(0, read));
            int lineEnd = head.IndexOf('\r');
            string request = lineEnd >= 0 ? head[..lineEnd] : head;
            bool ok = IsMetricsGet(request);
            string body;
            try
            {
                body = ok ? scrape() : "not found\n";
            }
            catch (Exception)
            {
                ok = false;
                body = "not found\n";
            }

            if (!body.EndsWith('\n'))
                body += "\n";
            byte[] payload = Encoding.UTF8.GetBytes(body);
            string status = ok ? "200 OK" : "404 Not Found";
            string type = ok ? "text/plain; version=0.0.4; charset=utf-8" : "text/plain; charset=utf-8";
            string headers =
                $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), ct).ConfigureAwait(false);
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    public static bool IsMetricsGet(string requestLine)
    {
        if (!requestLine.StartsWith("GET ", StringComparison.Ordinal))
            return false;
        int pathEnd = requestLine.IndexOf(' ', 4);
        if (pathEnd < 0)
            return false;
        string path = requestLine[4..pathEnd];
        int query = path.IndexOf('?');
        if (query >= 0)
            path = path[..query];
        return path is "/metrics" or "/metrics/";
    }
}
