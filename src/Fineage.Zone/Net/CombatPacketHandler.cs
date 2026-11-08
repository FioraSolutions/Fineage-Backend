using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Combat / target opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class CombatPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public CombatPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnAttackAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var attack = NetCodec.DecodeAttackRequest(envelope.Payload);
        string? error = await _world.TryAttackAsync(zoneSession, attack.TargetObjectId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Attack failed.", ct).ConfigureAwait(false);
        // AttackState / MoveState / StatusUpdate fan out via NetBridge from ZoneWorld combat.
    }

    public async Task OnUseSkillAsync(
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

        var use = NetCodec.DecodeUseSkill(envelope.Payload);
        string? error = await _world.TrySkillUseAsync(
            zoneSession,
            use.SkillId,
            use.TargetObjectId,
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "UseSkill failed.", ct).ConfigureAwait(false);
    }

    public async Task OnSelectTargetAsync(
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

        var select = NetCodec.DecodeSelectTarget(envelope.Payload);
        string? error = await _world.TrySelectTargetAsync(zoneSession, select.TargetObjectId, ct).ConfigureAwait(false);
        if (error != null)
        {
            await _reject(conn, error, "Select target failed.", ct).ConfigureAwait(false);
            return;
        }

        // Nothing to answer here: the world already fanned MyTargetSelected's Net mirror (TargetSelected, id +
        // Mobius colour), the target's StatusUpdate and the error path through INetPresenceBridge.
    }

    public async Task OnTargetCancelAsync(
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

        string? error = await _world.TryCancelTargetAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Target cancel failed.", ct).ConfigureAwait(false);
        // The world already fanned TargetUnselected, so nothing to answer here.
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
