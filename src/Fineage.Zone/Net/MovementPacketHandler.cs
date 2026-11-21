using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Movement opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// The knownlist spawn/despawn frames of a move are planned in the step that applies it and sent after the reply.
/// </summary>
public sealed class MovementPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;
    readonly NetKnownlistFanout _knownlist;

    internal MovementPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject,
        NetKnownlistFanout knownlist)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
        _knownlist = knownlist ?? throw new ArgumentNullException(nameof(knownlist));
    }

    readonly record struct MovePlan(string? Error, int X, int Y, int Z, List<NetFrame> Frames);

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
        var command = new MoveCommand(move.DestX, move.DestY, move.DestZ);
        var plan = await _world.ExecuteAroundAsync(zoneSession, command, apply =>
        {
            _world.BeginKnownlistDiff(zoneSession);
            try
            {
                string? error = apply();
                // MoveState to self + stayed peers is fanned by ZoneWorld.ReconcileKnownlist → NetBridge.
                var frames = error == null
                    ? _knownlist.PlanDiff(zoneSession, self, _world.KnownlistDiffBefore, _world.KnownlistDiffAfter)
                    : [];
                return new MovePlan(error, command.ResultX, command.ResultY, command.ResultZ, frames);
            }
            finally
            {
                _world.EndKnownlistDiff();
            }
        }, ct).ConfigureAwait(false);
        if (plan.Error != null)
        {
            await conn.SendAsync(
                NetOpcode.PositionCorrection,
                NetCodec.EncodePositionCorrection(new PositionCorrectionMsg
                {
                    EntityValue = self.Value,
                    EntityGeneration = self.Generation,
                    X = plan.X,
                    Y = plan.Y,
                    Z = plan.Z
                }),
                envelope.RequestSequence,
                ct).ConfigureAwait(false);
            return;
        }

        await _knownlist.SendAsync(conn, plan.Frames, ct).ConfigureAwait(false);
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
        string? drift = await _world.ExecuteAsync(zoneSession, new ValidatePositionCommand(vp.X, vp.Y, vp.Z), ct)
            .ConfigureAwait(false);
        if (drift == null)
            return;

        Console.WriteLine(
            $"[zone] ValidatePosition {drift} obj={zoneSession.ObjectId} client={vp.X},{vp.Y},{vp.Z} server={zoneSession.X},{zoneSession.Y},{zoneSession.Z} -> PositionCorrection");
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

        await _world.ExecuteAsync(zoneSession, new StopMoveCommand(), ct).ConfigureAwait(false);
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
