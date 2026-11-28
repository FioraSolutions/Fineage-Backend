using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.Security;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

public sealed partial class WorldEventPacketHandler
{
    public async Task OnSiegeListAsync(
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

        var request = NetCodec.DecodeSiegeListRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(zoneSession, new SiegeListCommand(request.CastleId), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Siege list failed.", ct).ConfigureAwait(false);
        // SiegeInfo goes out as fanout, so the reply body is empty on purpose.
    }

    public async Task OnSiegeRegisterAsync(
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

        var request = NetCodec.DecodeSiegeRegisterRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new SiegeRegisterCommand(
                new SiegeRegisterRequestMsg
                {
                    CastleId = request.CastleId,
                    Attacker = request.Attacker,
                    Joining = request.Joining
                }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Siege registration was rejected.", ct).ConfigureAwait(false);
    }

    public async Task OnSiegeAnswerAsync(
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

        var request = NetCodec.DecodeSiegeDefenderAnswerRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new SiegeAnswerCommand(
                new SiegeDefenderAnswerRequestMsg
                {
                    CastleId = request.CastleId,
                    ClanId = request.ClanId,
                    Approved = request.Approved
                }),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Siege answer was rejected.", ct).ConfigureAwait(false);
    }

    public async Task OnAdminCaptureCastleAsync(
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
            await _reject(conn, InventoryProtocol.Forbidden, "AdminCaptureCastle is disabled.", ct).ConfigureAwait(false);
            return;
        }

        if (!AdminCommandCatalog.TryGetNet("AdminCaptureCastle", out AdminCommandSpec spec))
            throw new InvalidOperationException("AdminCaptureCastle is missing from AdminCommandCatalog.");
        if (!_world.TryAuthorizeAdmin(zoneSession, spec.MinimumRole, spec.Name, ZoneWorld.AdminSourceNet, out string refusal))
        {
            await _reject(conn, refusal, "AdminCaptureCastle is forbidden for this character.", ct).ConfigureAwait(false);
            return;
        }

        var request = NetCodec.DecodeAdminCaptureCastle(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new AdminCaptureCommand(request.CastleId, request.ClanId, ZoneWorld.AdminSourceNet),
            ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "AdminCaptureCastle was rejected.", ct).ConfigureAwait(false);
    }

}
