using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class WorldEventPacketHandler
{
    public async Task OnOlympiadStatusAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryOlympiadStatusAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Olympiad status failed.", ct).ConfigureAwait(false);
        // OlympiadStatus goes out as fanout, so the reply body is empty on purpose.
    }

    public async Task OnOlympiadRegisterAsync(
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

        var request = NetCodec.DecodeOlympiadRegisterRequest(envelope.Payload);
        string? error = await _world.TryOlympiadRegisterAsync(
            zoneSession,
            new OlympiadRegisterRequestMsg { Classed = request.Classed },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Olympiad registration was rejected.", ct).ConfigureAwait(false);
    }

    public async Task OnOlympiadUnregisterAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryOlympiadUnregisterAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Olympiad unregister was rejected.", ct).ConfigureAwait(false);
    }

    public async Task OnOlympiadLeaveAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryOlympiadLeaveAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Olympiad leave was rejected.", ct).ConfigureAwait(false);
    }

    public async Task OnAdminReportOlympiadAsync(
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

        if (!_adminCommands)
        {
            await _reject(conn, InventoryProtocol.Forbidden, "AdminReportOlympiadResult is disabled.", ct).ConfigureAwait(false);
            return;
        }

        var request = NetCodec.DecodeAdminReportOlympiadResult(envelope.Payload);
        string? error = await _world.TryAdminReportOlympiadAsync(
            zoneSession,
            request.MatchId,
            request.WinnerObjectId,
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "AdminReportOlympiadResult was rejected.", ct).ConfigureAwait(false);
    }

}
