using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

public sealed class WeddingPacketHandler(
    ZoneWorld world,
    Func<NetConnection, string, string, CancellationToken, Task> reject)
{
    readonly ZoneWorld _world = world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject = reject;

    public async Task OnEngageAnswerAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        var answer = NetCodec.DecodeWeddingEngageAnswer(envelope.Payload);
        string? error = await _world.ExecuteAsync(
            zoneSession,
            new WeddingEngageAnswerCommand(answer.Response),
            ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Wedding engage answer failed.", ct).ConfigureAwait(false);
    }
}