using Fineage.Protocol.Telemetry;

namespace Fineage.Protocol;

/// <summary>
/// Loopback online-count contract between the Zone process and Login's public <c>GET /api/status</c>.
/// The listener is <c>127.0.0.1</c> only. It is not one of the public ports (2106, 7777, 5080).
/// </summary>
public static class ZoneStatus
{
    public const int DefaultPort = 7780;
    public const string PortVariable = "FINEAGE_ZONE_STATUS_PORT";
    public const string UrlVariable = "FINEAGE_ZONE_STATUS_URL";
    public const string Path = "/status";
    public const string PlayersProperty = "playersOnline";

    public static string LoopbackUrl(int port) => $"http://127.0.0.1:{port}{Path}";

    /// <summary>
    /// Unset means <see cref="DefaultPort"/>. <c>0</c> or <c>off</c> disables the listener.
    /// Garbage yields <c>0</c> and an error string.
    /// </summary>
    public static int ResolvePort(string? text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
            return DefaultPort;
        if (!MetricsEnv.TryParsePort(text, out int port, out error))
            return 0;
        return port;
    }

    /// <summary>
    /// An absolute http(s) <paramref name="urlText"/> wins over the port.
    /// Otherwise the loopback URL for <see cref="ResolvePort"/>.
    /// A null result means Login should report the zone as down.
    /// </summary>
    public static Uri? ResolveProbeUrl(string? urlText, string? portText, out string? error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(urlText))
        {
            string trimmed = urlText.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return uri;
            error = $"{UrlVariable} expected an absolute http(s) URL";
            return null;
        }

        int port = ResolvePort(portText, out string? portError);
        if (portError != null)
        {
            error = $"{PortVariable}: {portError}";
            return null;
        }

        if (port <= 0)
            return null;
        return new Uri(LoopbackUrl(port));
    }
}
