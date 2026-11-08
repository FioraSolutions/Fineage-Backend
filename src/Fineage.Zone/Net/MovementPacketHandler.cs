using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Movement opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// Knownlist AOI refresh after a successful move stays on the shared fanout callback.
/// </summary>
public sealed class MovementPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;
    readonly Func<NetConnection, ZoneSession, EntityId, HashSet<int>, HashSet<int>, CancellationToken, Task> _fanKnownlistDiff;

    public MovementPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject,
        Func<NetConnection, ZoneSession, EntityId, HashSet<int>, HashSet<int>, CancellationToken, Task> fanKnownlistDiff)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
        _fanKnownlistDiff = fanKnownlistDiff ?? throw new ArgumentNullException(nameof(fanKnownlistDiff));
    }

    public async Task OnMoveAsync(
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

        var move = NetCodec.DecodeMoveInput(envelope.Payload);
        var before = _world.SnapshotKnownIds(zoneSession);

        string? error = await _world.TryMoveAsync(
            zoneSession, move.DestX, move.DestY, move.DestZ, ct).ConfigureAwait(false);
        if (error != null)
        {
            await conn.SendAsync(
                NetOpcode.PositionCorrection,
                NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
                {
                    EntityValue = self.Value,
                    EntityGeneration = self.Generation,
                    X = zoneSession.X,
                    Y = zoneSession.Y,
                    Z = zoneSession.Z
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return;
        }

        var after = _world.SnapshotKnownIds(zoneSession);
        // MoveState to self + stayed peers is fanned by ZoneWorld.ReconcileKnownlist → NetBridge.
        await _fanKnownlistDiff(conn, zoneSession, self, before, after, ct).ConfigureAwait(false);
    }

    public async Task OnValidatePositionAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;

        var vp = NetCodec.DecodeValidatePosition(envelope.Payload);
        if (!_world.ValidateClientPosition(zoneSession, vp.X, vp.Y, vp.Z, out int dxy, out int dz))
            return;

        Console.WriteLine(
            $"[zone] ValidatePosition drift obj={zoneSession.ObjectId} dxy={dxy} dz={dz} client={vp.X},{vp.Y},{vp.Z} server={zoneSession.X},{zoneSession.Y},{zoneSession.Z} -> PositionCorrection");
        await conn.SendAsync(
            NetOpcode.PositionCorrection,
            NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
            {
                EntityValue = self.Value,
                EntityGeneration = self.Generation,
                X = zoneSession.X,
                Y = zoneSession.Y,
                Z = zoneSession.Z
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);
    }

    public async Task OnStopMoveAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        EntityId self,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        // Authoritative stop: correct to current session position.
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;

        _world.StopMove(zoneSession);
        await conn.SendAsync(
            NetOpcode.PositionCorrection,
            NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
            {
                EntityValue = self.Value,
                EntityGeneration = self.Generation,
                X = zoneSession.X,
                Y = zoneSession.Y,
                Z = zoneSession.Z
            }),
            envelope.RequestSequence,
            ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
