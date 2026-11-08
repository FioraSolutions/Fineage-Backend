using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Warehouse opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class WarehousePacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public WarehousePacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnDepositAsync(
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

        var req = NetCodec.DecodeWarehouseDepositRequest(envelope.Payload);
        var lines = (req.Lines ?? [])
            .Select(l => new Fineage.Game.Services.WarehouseLine(l.ObjectId, l.Count))
            .ToArray();
        string? error = await _world.TryWarehouseDepositAsync(
            zoneSession, req.NpcObjectId, lines, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Warehouse deposit failed.", ct).ConfigureAwait(false);
    }

    public async Task OnWithdrawAsync(
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

        var req = NetCodec.DecodeWarehouseWithdrawRequest(envelope.Payload);
        var lines = (req.Lines ?? [])
            .Select(l => new Fineage.Game.Services.WarehouseLine(l.ObjectId, l.Count))
            .ToArray();
        string? error = await _world.TryWarehouseWithdrawAsync(
            zoneSession, req.NpcObjectId, lines, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Warehouse withdraw failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
