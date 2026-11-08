using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Boss / siege / olympiad / seven signs / dungeon opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class WorldEventPacketHandler
{
    readonly ZoneWorld _world;
    readonly bool _adminCommands;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public WorldEventPacketHandler(
        ZoneWorld world,
        bool adminCommands,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _adminCommands = adminCommands;
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnBossStatusAsync(
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

        var request = NetCodec.DecodeBossStatusRequest(envelope.Payload);
        string? error = await _world.TryBossStatusAsync(zoneSession, request.NpcId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Boss status failed.", ct).ConfigureAwait(false);
        // The rows themselves go out as BossStatus fanout, so the reply body is empty on purpose.
    }

    public async Task OnAdminBossAsync(
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

        // Same gate the V2 listener used: the env flag is the only authority, not the request body.
        if (!_adminCommands)
        {
            await _reject(conn, InventoryProtocol.Forbidden, "AdminBossSchedule is disabled.", ct).ConfigureAwait(false);
            return;
        }

        var request = NetCodec.DecodeAdminBossSchedule(envelope.Payload);
        string? error = await _world.TryAdminBossNetAsync(zoneSession, request, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "AdminBossSchedule was rejected.", ct).ConfigureAwait(false);
    }

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
        string? error = await _world.TrySiegeListAsync(zoneSession, request.CastleId, ct).ConfigureAwait(false);
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
        string? error = await _world.TrySiegeRegisterAsync(
            zoneSession,
            new SiegeRegisterRequestMsg
            {
                CastleId = request.CastleId,
                Attacker = request.Attacker,
                Joining = request.Joining
            },
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
        string? error = await _world.TrySiegeAnswerAsync(
            zoneSession,
            new SiegeDefenderAnswerRequestMsg
            {
                CastleId = request.CastleId,
                ClanId = request.ClanId,
                Approved = request.Approved
            },
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

        var request = NetCodec.DecodeAdminCaptureCastle(envelope.Payload);
        string? error = await _world.TryAdminCaptureNetAsync(zoneSession, request, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "AdminCaptureCastle was rejected.", ct).ConfigureAwait(false);
    }

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

    public async Task OnSevenSignsStatusAsync(
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

        string? error = await _world.TrySevenSignsStatusAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "SevenSigns status failed.", ct).ConfigureAwait(false);
        // The record and the sky go out as fanout, so the reply body is empty on purpose.
    }

    public async Task OnSevenSignsActionAsync(
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

        var request = NetCodec.DecodeSevenSignsActionRequest(envelope.Payload);
        string? error = await _world.TrySevenSignsActionAsync(
            zoneSession,
            new SevenSignsActionRequestMsg
            {
                Action = request.Action,
                Cabal = request.Cabal,
                Seal = request.Seal,
                BlueStones = request.BlueStones,
                GreenStones = request.GreenStones,
                RedStones = request.RedStones
            },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "SevenSigns action was rejected.", ct).ConfigureAwait(false);
    }

    public async Task OnDungeonAsync(
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

        var request = NetCodec.DecodeDungeonEnterRequest(envelope.Payload);
        string? error = await _world.TryDungeonAsync(
            zoneSession,
            new DungeonEnterRequestMsg { DungeonId = request.DungeonId, Action = request.Action },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Dungeon request was rejected.", ct).ConfigureAwait(false);
        // DungeonEntered goes out as fanout, so the reply body is empty on purpose.
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
