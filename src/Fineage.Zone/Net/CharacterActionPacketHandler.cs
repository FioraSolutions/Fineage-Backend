using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Revive / HTML bypass opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// AOI after a teleport-style outcome is fanned once by <c>ZoneWorld.Relocate</c> through the NetBridge
/// (spawn / despawn for both sides); a second handler-side diff here sent every spawn/despawn twice.
/// </summary>
public sealed class CharacterActionPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public CharacterActionPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnReviveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var revive = NetCodec.DecodeReviveRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(zoneSession, new ReviveCommand(revive.Action), ct).ConfigureAwait(false);
        if (error != null)
        {
            await _reject(conn, error, "Revive failed.", ct).ConfigureAwait(false);
            return;
        }

        // Revive / Teleport / StatusUpdate and the village relocate's spawn/despawn all fan via NetBridge.
    }

    public async Task OnBypassAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var bypass = NetCodec.DecodeBypassRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new BypassCommand(bypass.NpcObjectId, bypass.Command),
            ct).ConfigureAwait(false);
        if (error != null)
        {
            await _reject(conn, error, "Bypass failed.", ct).ConfigureAwait(false);
        }

        // A teleport bypass relocates; Relocate already fanned the AOI diff to the jumper and its peers.
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
