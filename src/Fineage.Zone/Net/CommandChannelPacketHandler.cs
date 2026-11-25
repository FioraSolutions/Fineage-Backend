using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

public sealed class CommandChannelPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public CommandChannelPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnAskAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var ask = NetCodec.DecodeCommandChannelAskRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new CommandChannelAskCommand(ask.TargetName),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Command channel invite failed.", ct).ConfigureAwait(false);
    }

    public async Task OnAcceptAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var accept = NetCodec.DecodeCommandChannelAcceptRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new CommandChannelAcceptCommand(accept.Response),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Command channel answer failed.", ct).ConfigureAwait(false);
    }

    public async Task OnOustAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound || !zoneSession.InWorld)
        {
            await _reject(conn, "not_in_world", "Enter world first.", ct).ConfigureAwait(false);
            return;
        }

        var oust = NetCodec.DecodeCommandChannelOustRequest(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new CommandChannelOustCommand(oust.TargetName),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Command channel oust failed.", ct).ConfigureAwait(false);
    }

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }
}