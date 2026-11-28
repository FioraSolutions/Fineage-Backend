using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

public sealed partial class WorldEventPacketHandler
{
    public async Task OnPCCafeCouponUseAsync(
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

        var request = NetCodec.DecodePCCafeCouponUse(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new PCCafeCouponUseCommand(request.Code ?? ""),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "PC Cafe coupon was rejected.", ct).ConfigureAwait(false);
    }
}
