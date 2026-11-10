using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Revive / HTML bypass opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// AOI refresh after teleport-style outcomes uses the shared knownlist fanout callback.
/// </summary>
public sealed class CharacterActionPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;
    readonly Func<NetConnection, ZoneSession, EntityId, HashSet<int>, HashSet<int>, CancellationToken, Task> _fanKnownlistDiff;

    public CharacterActionPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject,
        Func<NetConnection, ZoneSession, EntityId, HashSet<int>, HashSet<int>, CancellationToken, Task> fanKnownlistDiff)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
        _fanKnownlistDiff = fanKnownlistDiff ?? throw new ArgumentNullException(nameof(fanKnownlistDiff));
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

        var before = _world.SnapshotKnownIds(zoneSession);
        var revive = NetCodec.DecodeReviveRequest(envelope.Payload);
        string? error = await _world.TryReviveAsync(zoneSession, revive.Action, ct).ConfigureAwait(false);
        if (error != null)
        {
            await _reject(conn, error, "Revive failed.", ct).ConfigureAwait(false);
            return;
        }

        // Revive / Teleport / StatusUpdate fan via NetBridge; AOI after village relocate needs spawn/despawn.
        var after = _world.SnapshotKnownIds(zoneSession);
        await _fanKnownlistDiff(conn, zoneSession, self, before, after, ct).ConfigureAwait(false);
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

        var before = _world.SnapshotKnownIds(zoneSession);
        var bypass = NetCodec.DecodeBypassRequest(envelope.Payload);
        string? error = await _world.TryBypassAsync(
            zoneSession,
            bypass.NpcObjectId,
            bypass.Command,
            ct).ConfigureAwait(false);
        if (error != null)
        {
            await _reject(conn, error, "Bypass failed.", ct).ConfigureAwait(false);
            return;
        }

        // Teleport bypass relocates; refresh AOI for Net peers.
        var after = _world.SnapshotKnownIds(zoneSession);
        await _fanKnownlistDiff(conn, zoneSession, self, before, after, ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
