using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

public sealed class SkillEnchantPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public SkillEnchantPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnInfoAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var msg = NetCodec.DecodeEnchantSkillRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new EnchantSkillInfoCommand(msg.SkillId, msg.SkillLevel),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Enchant info failed.", ct).ConfigureAwait(false);
    }

    public async Task OnEnchantAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var msg = NetCodec.DecodeEnchantSkillRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new EnchantSkillCommand(msg.SkillId, msg.SkillLevel),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Enchant failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}