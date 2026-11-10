using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Friend / block opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class FriendPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public FriendPacketHandler(
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

        var req = NetCodec.DecodeFriendInvite(envelope.Payload);
        string? error = await _world.TryFriendInviteAsync(
            zoneSession,
            new FriendInviteMsg { Name = req.Name ?? "" },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Friend invite failed.", ct).ConfigureAwait(false);
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

        var req = NetCodec.DecodeFriendAnswer(envelope.Payload);
        string? error = await _world.TryFriendAnswerAsync(
            zoneSession,
            new FriendAnswerMsg { Response = req.Response },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Friend answer failed.", ct).ConfigureAwait(false);
    }

    public async Task OnListRequestAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.TryFriendListAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Friend list failed.", ct).ConfigureAwait(false);
    }

    public async Task OnDeleteAsync(
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

        var req = NetCodec.DecodeFriendDelete(envelope.Payload);
        string? error = await _world.TryFriendDeleteAsync(
            zoneSession,
            new FriendDeleteMsg { Name = req.Name ?? "" },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Friend delete failed.", ct).ConfigureAwait(false);
    }

    public async Task OnBlockAsync(
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

        var req = NetCodec.DecodeBlockRequest(envelope.Payload);
        string? error = await _world.TryBlockRequestAsync(
            zoneSession,
            new BlockRequestMsg { Type = req.Type, Name = req.Name ?? "" },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Block request failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
