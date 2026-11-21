using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

public sealed class AugmentPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public AugmentPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnConfirmTargetAsync(NetConnection conn, ZoneSession session, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!session.Bound || !session.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var msg = NetCodec.DecodeAugmentConfirmTarget(envelope.Payload);
        string? error = await _world.ExecuteAsync(session, new AugmentConfirmTargetCommand(msg.TargetObjectId), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Augment target confirm failed.", ct).ConfigureAwait(false);
    }

    public async Task OnConfirmRefinerAsync(NetConnection conn, ZoneSession session, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!session.Bound || !session.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var msg = NetCodec.DecodeAugmentConfirmRefiner(envelope.Payload);
        string? error = await _world.ExecuteAsync(session, new AugmentConfirmRefinerCommand(msg.TargetObjectId, msg.RefinerObjectId), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Augment refiner confirm failed.", ct).ConfigureAwait(false);
    }

    public async Task OnConfirmGemAsync(NetConnection conn, ZoneSession session, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!session.Bound || !session.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var msg = NetCodec.DecodeAugmentConfirmGem(envelope.Payload);
        string? error = await _world
            .ExecuteAsync(session, new AugmentConfirmGemCommand(msg.TargetObjectId, msg.RefinerObjectId, msg.GemstoneObjectId, msg.GemstoneCount), ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Augment gem confirm failed.", ct).ConfigureAwait(false);
    }

    public async Task OnRefineAsync(NetConnection conn, ZoneSession session, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!session.Bound || !session.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var msg = NetCodec.DecodeAugmentRefine(envelope.Payload);
        string? error = await _world
            .ExecuteAsync(session, new AugmentRefineCommand(msg.TargetObjectId, msg.RefinerObjectId, msg.GemstoneObjectId, msg.GemstoneCount), ct)
            .ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Augment refine failed.", ct).ConfigureAwait(false);
    }

    public async Task OnRefineCancelAsync(NetConnection conn, ZoneSession session, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!session.Bound || !session.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var msg = NetCodec.DecodeAugmentRefineCancel(envelope.Payload);
        string? error = await _world.ExecuteAsync(session, new AugmentRefineCancelCommand(msg.TargetObjectId), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Augment remove failed.", ct).ConfigureAwait(false);
    }

    public async Task OnCancelWindowAsync(NetConnection conn, ZoneSession session, CancellationToken ct)
    {
        RequireHello(conn);
        if (!session.Bound || !session.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        string? error = await _world.ExecuteAsync(session, new AugmentCancelWindowCommand(), ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Augment window cancel failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}