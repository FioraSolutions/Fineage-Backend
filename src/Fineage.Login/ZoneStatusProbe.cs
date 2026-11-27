using System.Net;
using System.Net.Http;
using System.Text.Json;
using Fineage.Protocol;
using Fineage.Accounts;

namespace Fineage.Login;

public readonly record struct ZoneStatusReading(
    bool Reachable,
    int PlayersOnline,
    IReadOnlyList<string>? FailureReasons = null)
{
    public static ZoneStatusReading Down => default;

    public static ZoneStatusReading Unreachable(string reason) => new(false, 0, [reason]);

    public IReadOnlyList<string> Reasons => FailureReasons ?? [];
}

/// <summary>Where Login learns whether Zone is up and how many players are in the world.</summary>
public interface IZoneStatusSource
{
    Task<ZoneStatusReading> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Used when the zone status port is off or the probe URL is invalid. Always reports the zone down.</summary>
public sealed class UnavailableZoneStatus : IZoneStatusSource
{
    public static UnavailableZoneStatus Instance { get; } = new();

    UnavailableZoneStatus()
    {
    }

    public Task<ZoneStatusReading> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ZoneStatusReading.Down);
}

/// <summary>
/// GETs Zone's loopback <c>/status</c>. A miss, timeout, or unreadable body is zone down with zero players.
/// The host that starts this probe owns it and disposes it.
/// </summary>
public sealed class HttpZoneStatusSource : IZoneStatusSource, IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);

    readonly HttpClient _http;

    public HttpZoneStatusSource(Uri url, TimeSpan timeout)
        : this(url, timeout, CreateHandler(timeout))
    {
    }

    internal HttpZoneStatusSource(Uri url, TimeSpan timeout, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(handler);
        Url = url;
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
    }

    public Uri Url { get; }

    public static HttpZoneStatusSource? FromEnvironment(TextWriter? error = null)
    {
        Uri? url = ZoneStatus.ResolveProbeUrl(
            Environment.GetEnvironmentVariable(ZoneStatus.UrlVariable),
            Environment.GetEnvironmentVariable(ZoneStatus.PortVariable),
            out string? problem);
        if (!string.IsNullOrEmpty(problem))
            error?.WriteLine("[status] " + problem);
        return url == null ? null : new HttpZoneStatusSource(url, DefaultTimeout);
    }

    public async Task<ZoneStatusReading> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _http
                .GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable && !response.IsSuccessStatusCode)
                return ZoneStatusReading.Down;

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[2048];
            int total = 0;
            while (total < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                    .ConfigureAwait(false);
                if (n == 0)
                    break;
                total += n;
            }

            if (total == 0 || total == buffer.Length)
                return ZoneStatusReading.Down;
            return Parse(buffer.AsSpan(0, total).ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ZoneStatusReading.Down;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException)
        {
            return ZoneStatusReading.Down;
        }
    }

    public void Dispose() => _http.Dispose();

    static SocketsHttpHandler CreateHandler(TimeSpan timeout) => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = timeout,
        PooledConnectionLifetime = TimeSpan.FromSeconds(2)
    };

    static ZoneStatusReading Parse(byte[] utf8)
    {
        try
        {
            using var doc = JsonDocument.Parse(utf8);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ZoneStatusReading.Unreachable("zone status body is not a JSON object");
            int? count = null;
            bool? ready = null;
            var reasons = new List<string>();
            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (prop.Name.Equals(ZoneStatus.PlayersProperty, StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out int parsed))
                        return ZoneStatusReading.Unreachable("zone status playersOnline is not an integer");
                    count = parsed;
                }
                else if (prop.Name.Equals("ready", StringComparison.OrdinalIgnoreCase))
                {
                    ready = prop.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => null
                    };
                }
                else if (prop.Name.Equals("reasons", StringComparison.OrdinalIgnoreCase) &&
                         prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement reason in prop.Value.EnumerateArray())
                    {
                        if (reason.ValueKind == JsonValueKind.String)
                            reasons.Add(reason.GetString() ?? "");
                    }
                }
            }

            if (count is not int players)
                return ZoneStatusReading.Unreachable("zone status body is missing playersOnline");
            // Legacy /status deliberately contains only playersOnline. New readiness is optional here;
            // operations use /readyz directly, so extending status never breaks older Zone probes.
            if (ready is false)
                return ZoneStatusReading.Unreachable(reasons.Count == 0 ? "zone reported not ready" : string.Join("; ", reasons));
            if (players < 0)
                players = 0;
            return new ZoneStatusReading(true, players);
        }
        catch (JsonException)
        {
            return ZoneStatusReading.Unreachable("zone status body is not valid JSON");
        }
    }
}
