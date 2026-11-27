using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>Mobius community board opcodes (#929).</summary>
public sealed class CommunityBoardPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public CommunityBoardPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnShowBoardAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        _ = NetCodec.DecodeShowBoardRequest(envelope.Payload);
        string? error = await _world.TryShowBoardAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Community board open failed.", ct).ConfigureAwait(false);
    }

    public async Task OnBBSwriteAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodeBBSwrite(envelope.Payload);
        string? error = await _world.TryBBSwriteAsync(zoneSession, new BBSwriteMsg
        {
            Url = net.Url ?? "",
            Arg1 = net.Arg1 ?? "",
            Arg2 = net.Arg2 ?? "",
            Arg3 = net.Arg3 ?? "",
            Arg4 = net.Arg4 ?? "",
            Arg5 = net.Arg5 ?? ""
        }, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Community board write failed.", ct).ConfigureAwait(false);
    }
}
