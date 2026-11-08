using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Acquire-skill opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class SkillLearnPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public SkillLearnPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnAcquireAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        bool informing,
        CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var (skillId, level, skillType) = NetCodec.DecodeAcquireSkillRequest(envelope.Payload);
        string? error = await _world.TryAcquireSkillAsync(zoneSession, skillId, level, skillType, informing, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, informing ? "Skill info failed." : "Learn failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}
