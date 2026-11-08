using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Say opcode extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class ChatPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<DateTimeOffset> _clock;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public ChatPacketHandler(
        ZoneWorld world,
        Func<DateTimeOffset> clock,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnSayAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var say = NetCodec.DecodeSay(envelope.Payload);
        string? error = await _world.TrySayAsync(
            zoneSession, say.Text, say.ChatType, _clock(), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Say failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
