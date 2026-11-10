using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Boss / siege / olympiad / seven signs / dungeon opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed partial class WorldEventPacketHandler
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
        string? error = await _world.TryAdminBossAsync(
            zoneSession,
            new AdminBossScheduleMsg { NpcId = request.NpcId, Action = request.Action },
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "AdminBossSchedule was rejected.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
