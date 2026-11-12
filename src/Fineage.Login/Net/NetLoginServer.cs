using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Accounts;

namespace Fineage.Login.Net;

/// <summary>Fineage Net login path — calls <see cref="LoginService"/> directly, no V2/Interlude wire.</summary>
public sealed partial class NetLoginServer
{
    readonly LoginService _service;
    readonly Func<DateTimeOffset> _clock;
    readonly string _zoneHost;
    readonly int _zonePort;
    readonly int _maxMessagesPerSecond;
    readonly TimeSpan _idleTimeout;

    public NetLoginServer(
        LoginService service,
        string? zoneHost = null,
        int? zonePort = null,
        Func<DateTimeOffset>? clock = null,
        int? maxMessagesPerSecond = null,
        TimeSpan? idleTimeout = null)
    {
        _service = service;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _zoneHost = string.IsNullOrWhiteSpace(zoneHost) ? "127.0.0.1" : zoneHost.Trim();
        _zonePort = zonePort is > 0 and <= 65535 ? zonePort.Value : NetSettings.ZonePort;
        _maxMessagesPerSecond = maxMessagesPerSecond ?? LoginNetSettings.MaxMessagesPerSecond;
        _idleTimeout = idleTimeout ?? TcpSessionSettings.IdleTimeout;
    }
}
