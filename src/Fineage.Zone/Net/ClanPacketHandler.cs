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
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public ClanPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
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
            .ExecuteAsync(
                zoneSession,
                new ClanCreateCommand(new ClanCreateRequestMsg { Name = create.Name }),
                ct)
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
            .ExecuteAsync(
                zoneSession,
                new ClanInviteCommand(
                    new ClanInviteRequestMsg { TargetObjectId = invite.TargetObjectId, PledgeType = invite.PledgeType }),
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
            .ExecuteAsync(
                zoneSession,
                new ClanAnswerCommand(
                    new ClanAnswerJoinRequestMsg
                    {
                        RequestorObjectId = answer.RequestorObjectId,
                        Response = answer.Response
                    }),
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

        string? error = await _world.ExecuteAsync(zoneSession, new ClanLeaveCommand(), ct).ConfigureAwait(false);
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
            .ExecuteAsync(
                zoneSession,
                new ClanKickCommand(new ClanKickRequestMsg { TargetObjectId = kick.TargetObjectId }),
                ct)
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

        string? error = await _world.ExecuteAsync(zoneSession, new ClanDissolveCommand(), ct).ConfigureAwait(false);
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

        string? error = await _world.ExecuteAsync(zoneSession, new ClanCancelDissolveCommand(), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan cancel dissolve failed.", ct).ConfigureAwait(false);
    }

    /// <summary><see cref="NetOpcode.PledgeCommand"/>: one clan / alliance window action.</summary>
    public async Task OnPledgeCommandAsync(
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

        var command = NetCodec.DecodePledgeCommand(envelope.Payload);
        string? error = await _world.ExecuteAsync(zoneSession, new PledgeCommand(command), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Clan command failed.", ct).ConfigureAwait(false);
    }

    /// <summary><see cref="NetOpcode.CastleCommand"/>: one chamberlain (castle lord) action.</summary>
    public async Task OnCastleCommandAsync(
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

        var command = NetCodec.DecodeCastleCommand(envelope.Payload);
        string? error = await _world.ExecuteAsync(zoneSession, new CastleCommand(command), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Castle command failed.", ct).ConfigureAwait(false);
    }

    /// <summary><see cref="NetOpcode.ManorCommand"/>: one manor manager / castle lord manor action.</summary>
    public async Task OnManorCommandAsync(
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

        var command = NetCodec.DecodeManorCommand(envelope.Payload);
        string? error = await _world.ExecuteAsync(zoneSession, new ManorCommand(command), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Manor command failed.", ct).ConfigureAwait(false);
    }

    /// <summary>Summon / pet orders (Interlude RequestActionUse pet actions, RequestPetMove, …).</summary>
    public async Task OnSummonCommandAsync(
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

        var command = NetCodec.DecodeSummonCommand(envelope.Payload);
        string? error = await _world.ExecuteAsync(zoneSession, new SummonCommand(command), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Summon command failed.", ct).ConfigureAwait(false);
    }

    /// <summary>/duel, /partyduel, duel answer, /surrender (Mobius RequestDuelStart / RequestDuelAnswerStart).</summary>
    public async Task OnDuelCommandAsync(
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

        var command = NetCodec.DecodeDuelCommand(envelope.Payload);
        string? error = await _world.ExecuteAsync(zoneSession, new DuelCommand(command), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Duel command failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}