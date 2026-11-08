using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Pickup / drop / destroy / use / unequip opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class InventoryActionPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public InventoryActionPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnPickupAsync(
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

        var pickup = NetCodec.DecodePickupRequest(envelope.Payload);
        string? error = await _world.TryPickupAsync(zoneSession, pickup.GroundObjectId, ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Pickup failed.", ct).ConfigureAwait(false);
        // InventoryUpdate + EntityDespawn fan out via NetBridge.
    }

    public async Task OnDestroyAsync(
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

        var destroy = NetCodec.DecodeDestroyItemRequest(envelope.Payload);
        string? error = await _world.TryDestroyItemAsync(
            zoneSession, destroy.ObjectId, destroy.Count, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Destroy item failed.", ct).ConfigureAwait(false);
        // InventoryUpdate fans out from ZoneWorldGround.
    }

    public async Task OnDropAsync(
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

        var drop = NetCodec.DecodeDropItemRequest(envelope.Payload);
        string? error = await _world.TryDropItemAsync(
            zoneSession, drop.ObjectId, drop.Count, drop.X, drop.Y, drop.Z, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Drop failed.", ct).ConfigureAwait(false);
    }

    public async Task OnUseItemAsync(
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

        var use = NetCodec.DecodeUseItem(envelope.Payload);
        string? error = await _world.TryUseItemAsync(zoneSession, use.ObjectId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "UseItem failed.", ct).ConfigureAwait(false);
    }

    public async Task OnUnequipAsync(
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

        var unequip = NetCodec.DecodeUnequipItem(envelope.Payload);
        string? error = await _world.TryUnequipItemAsync(zoneSession, unequip.PaperdollSlot, ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Unequip failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
