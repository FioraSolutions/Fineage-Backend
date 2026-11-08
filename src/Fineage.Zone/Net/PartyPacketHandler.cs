using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Party opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class PartyPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public PartyPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
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

        var invite = NetCodec.DecodePartyInviteRequest(envelope.Payload);
        string? error = await _world.TryPartyInviteAsync(
            zoneSession, invite.TargetObjectId, invite.ItemDistribution, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party invite failed.", ct).ConfigureAwait(false);
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

        var answer = NetCodec.DecodePartyAnswerJoinRequest(envelope.Payload);
        string? error = await _world.TryPartyAnswerAsync(
            zoneSession, answer.RequestorObjectId, answer.Response, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party answer failed.", ct).ConfigureAwait(false);
    }

    public async Task OnLeaveAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryPartyLeaveAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party leave failed.", ct).ConfigureAwait(false);
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

        var kick = NetCodec.DecodePartyKickRequest(envelope.Payload);
        string? error = await _world.TryPartyKickAsync(
            zoneSession, kick.TargetObjectId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party kick failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
