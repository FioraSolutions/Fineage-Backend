using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

public sealed class FreightPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public FreightPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnSendableListAsync(
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

        var req = NetCodec.DecodePackageSendableListRequest(envelope.Payload);
        string? error = await _world.TryPackageSendableListAsync(zoneSession, req.RecipientObjectId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Package sendable list failed.", ct).ConfigureAwait(false);
    }

    public async Task OnSendAsync(
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

        var req = NetCodec.DecodePackageSendRequest(envelope.Payload);
        var lines = (req.Lines ?? [])
            .Select(l => new WarehouseLine(l.ObjectId, l.Count))
            .ToArray();
        string? error = await _world.TryPackageSendAsync(zoneSession, req.RecipientObjectId, lines, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Package send failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}