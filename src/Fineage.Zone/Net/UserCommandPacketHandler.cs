using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>Retail <c>RequestBypassUserCmd</c> (0xaa). The body is one command id.</summary>
public sealed class UserCommandPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public UserCommandPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnBypassAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var req = NetCodec.DecodeBypassUserCmd(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new UserCommandCommand(req.CommandId),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "User command failed.", ct).ConfigureAwait(false);
    }
}
