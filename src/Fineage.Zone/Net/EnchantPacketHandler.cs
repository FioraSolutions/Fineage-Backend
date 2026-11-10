using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Enchant opcode group extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// Decode → world command; rejects stay on the shared zone reply helper.
/// </summary>
public sealed class EnchantPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public EnchantPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnRequestAsync(
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

        var request = NetCodec.DecodeEnchantRequest(envelope.Payload);
        string? error = await _world
            .TryEnchantRequestAsync(zoneSession, request.ScrollObjectId, ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Enchant scroll select failed.", ct).ConfigureAwait(false);
    }

    public async Task OnConfirmAsync(
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

        var confirm = NetCodec.DecodeEnchantConfirm(envelope.Payload);
        string? error = await _world
            .TryEnchantConfirmAsync(zoneSession, confirm.TargetObjectId, ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Enchant failed.", ct).ConfigureAwait(false);
    }

    public async Task OnCancelAsync(
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

        string? error = await _world.TryEnchantCancelAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Enchant cancel failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
