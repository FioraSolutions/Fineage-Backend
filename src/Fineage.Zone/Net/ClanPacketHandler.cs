using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Clan opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class ClanPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<DateTimeOffset> _clock;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public ClanPacketHandler(
        ZoneWorld world,
        Func<DateTimeOffset> clock,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnCreateAsync(
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

        var create = NetCodec.DecodeClanCreateRequest(envelope.Payload);
        string? error = await _world
            .TryClanCreateAsync(zoneSession, new ClanCreateRequestMsg { Name = create.Name }, _clock(), ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan create failed.", ct).ConfigureAwait(false);
    }

    public async Task OnInviteAsync(
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

        var invite = NetCodec.DecodeClanInviteRequest(envelope.Payload);
        string? error = await _world
            .TryClanInviteAsync(
                zoneSession,
                new ClanInviteRequestMsg { TargetObjectId = invite.TargetObjectId, PledgeType = 0 },
                _clock(),
                ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan invite failed.", ct).ConfigureAwait(false);
    }

    public async Task OnAnswerAsync(
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

        var answer = NetCodec.DecodeClanAnswer(envelope.Payload);
        string? error = await _world
            .TryClanAnswerAsync(
                zoneSession,
                new ClanAnswerJoinRequestMsg
                {
                    RequestorObjectId = answer.RequestorObjectId,
                    Response = answer.Response
                },
                _clock(),
                ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan answer failed.", ct).ConfigureAwait(false);
    }

    public async Task OnLeaveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryClanLeaveAsync(zoneSession, _clock(), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan leave failed.", ct).ConfigureAwait(false);
    }

    public async Task OnKickAsync(
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

        var kick = NetCodec.DecodeClanKickRequest(envelope.Payload);
        string? error = await _world
            .TryClanKickAsync(zoneSession, new ClanKickRequestMsg { TargetObjectId = kick.TargetObjectId }, _clock(), ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan kick failed.", ct).ConfigureAwait(false);
    }

    public async Task OnDissolveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryClanDissolveAsync(zoneSession, _clock(), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan dissolve failed.", ct).ConfigureAwait(false);
    }

    public async Task OnCancelDissolveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryClanCancelDissolveAsync(zoneSession, _clock(), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan cancel dissolve failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
