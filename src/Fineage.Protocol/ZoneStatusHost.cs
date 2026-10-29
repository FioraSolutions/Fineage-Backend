using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Fineage.Protocol;

/// <summary>
/// Tiny loopback HTTP server. <c>GET /status</c> returns <c>{"playersOnline":n}</c> from the zone simulation.
/// </summary>
public sealed class ZoneStatusHost : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts;
    readonly Task _loop;

    ZoneStatusHost(TcpListener listener, CancellationTokenSource cts, Task loop, int port)
    {
        _listener = listener;
        _cts = cts;
        _loop = loop;
        Port = port;
    }

    public int Port { get; }

    public string Url => ZoneStatus.LoopbackUrl(Port);

    /// <summary>Binds <c>127.0.0.1</c>. Port <c>0</c> picks an ephemeral port. Returns null when the bind fails.</summary>
    public static ZoneStatusHost? TryStart(int port, Func<int> playersOnline, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(playersOnline);
        ArgumentNullException.ThrowIfNull(error);
        if (port is < 0 or > 65535)
        {
            error.WriteLine($"[ZONE] status port {port} is invalid");
            return null;
        }

        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            error.WriteLine($"[ZONE] status {ZoneStatus.LoopbackUrl(port)} failed: {ex.Message}");
            return null;
        }

        int bound = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cts = new CancellationTokenSource();
        var loop = Task.Run(() => AcceptAsync(listener, playersOnline, cts.Token));
        return new ZoneStatusHost(listener, cts, loop, bound);
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

    public static bool IsStatusGet(string requestLine)
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
        return path is "/status" or "/status/";
    }

    static async Task AcceptAsync(TcpListener listener, Func<int> playersOnline, CancellationToken ct)
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

                _ = Task.Run(() => RespondAsync(client, playersOnline, ct), ct);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    static async Task RespondAsync(TcpClient client, Func<int> playersOnline, CancellationToken ct)
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
            if (!IsStatusGet(request))
            {
                await WriteAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "not found\n", ct).ConfigureAwait(false);
                return;
            }

            string body;
            string status;
            string type;
            try
            {
                int count = playersOnline();
                if (count < 0)
                    count = 0;
                body = "{\"" + ZoneStatus.PlayersProperty + "\":" + count.ToString(CultureInfo.InvariantCulture) + "}";
                status = "200 OK";
                type = "application/json; charset=utf-8";
            }
            catch (Exception)
            {
                body = "unavailable\n";
                status = "503 Service Unavailable";
                type = "text/plain; charset=utf-8";
            }

            await WriteAsync(stream, status, type, body, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    static async Task WriteAsync(NetworkStream stream, string status, string contentType, string body, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string headers =
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
