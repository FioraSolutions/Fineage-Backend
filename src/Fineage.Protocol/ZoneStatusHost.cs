using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Fineage.Protocol.Health;

namespace Fineage.Protocol;

/// <summary>
/// Tiny loopback HTTP server for Zone operations. <c>GET /status</c> returns the established compact
/// play-landing count, <c>GET /livez</c> is cheap process liveness, and <c>GET /readyz</c> is the deploy
/// contract: 200 only when every required dependency and startup stage is ready, otherwise 503 with reasons.
/// </summary>
public sealed class ZoneStatusHost : IAsyncDisposable
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    /// <summary>
    /// Binds <c>127.0.0.1</c>. Port <c>0</c> picks an ephemeral port. Returns null when the bind fails.
    /// A null <paramref name="readiness"/> reports a failed probe; it never defaults to healthy.
    /// </summary>
    public static ZoneStatusHost? TryStart(
        int port,
        Func<int> playersOnline,
        TextWriter error,
        Func<ValueTask<ReadinessReport>>? readiness = null)
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
        var loop = Task.Run(() => AcceptAsync(listener, playersOnline, readiness, cts.Token));
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

    public static bool IsStatusGet(string requestLine) =>
        TryGetPath(requestLine, out string path) && path is "/status" or "/status/";

    static bool TryGetPath(string requestLine, out string path)
    {
        path = "";
        if (!requestLine.StartsWith("GET ", StringComparison.Ordinal))
            return false;
        int pathEnd = requestLine.IndexOf(' ', 4);
        if (pathEnd < 0)
            return false;
        path = requestLine[4..pathEnd];
        int query = path.IndexOf('?');
        if (query >= 0)
            path = path[..query];
        return true;
    }

    static async Task AcceptAsync(
        TcpListener listener,
        Func<int> playersOnline,
        Func<ValueTask<ReadinessReport>>? readiness,
        CancellationToken ct)
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

                _ = Task.Run(() => RespondAsync(client, playersOnline, readiness, ct), ct);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    static async Task RespondAsync(
        TcpClient client,
        Func<int> playersOnline,
        Func<ValueTask<ReadinessReport>>? readiness,
        CancellationToken ct)
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
            if (!TryGetPath(request, out string path))
            {
                await WriteAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "not found\n", ct).ConfigureAwait(false);
                return;
            }

            ReadinessReport report = await ReadReadinessAsync(readiness).ConfigureAwait(false);
            if (path is "/livez" or "/livez/" or "/healthz" or "/healthz/")
            {
                await WriteAsync(stream, "200 OK", "application/json; charset=utf-8",
                    HealthBody.Live("zone").ToJson(), ct).ConfigureAwait(false);
                return;
            }

            if (path is "/readyz" or "/readyz/")
            {
                HealthBody body = HealthBody.From("zone", report);
                await WriteAsync(stream, body.Ready ? "200 OK" : "503 Service Unavailable",
                    "application/json; charset=utf-8", body.ToJson(), ct).ConfigureAwait(false);
                return;
            }

            if (path is not ("/status" or "/status/"))
            {
                await WriteAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "not found\n", ct).ConfigureAwait(false);
                return;
            }

            try
            {
                int count = playersOnline();
                if (count < 0)
                    count = 0;
                // Keep the established compact status contract used by Login and the play landing.
                // Detailed readiness has its own /readyz endpoint; /status must stay compatible.
                await WriteAsync(stream, "200 OK", "application/json; charset=utf-8",
                    JsonSerializer.Serialize(new { playersOnline = count }, Json), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var failed = new ReadinessReport(false,
                    [.. report.Checks, new ReadinessCheck("players", false, "playersOnline callback failed: " + ex.Message)]);
                HealthBody body = HealthBody.From("zone", failed, playersOnline: 0);
                await WriteAsync(stream, "503 Service Unavailable", "application/json; charset=utf-8", body.ToJson(), ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    static async ValueTask<ReadinessReport> ReadReadinessAsync(Func<ValueTask<ReadinessReport>>? readiness)
    {
        if (readiness == null)
            return ReadinessReport.NotConfigured("zone");
        try
        {
            return await readiness().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReadinessReport(false,
                [new ReadinessCheck("probe", false, "zone readiness probe threw: " + ex.Message)]);
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
