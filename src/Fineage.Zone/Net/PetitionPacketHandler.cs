using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>Mobius petition opcodes (#930).</summary>
public sealed class PetitionPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public PetitionPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnPetitionAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePetitionRequest(envelope.Payload);
        string? error = await _world.TryPetitionAsync(zoneSession, new PetitionRequestMsg
        {
            Content = net.Content ?? "",
            Type = net.Type
        }, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Petition failed.", ct).ConfigureAwait(false);
    }

    public async Task OnCancelAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        string? error = await _world.TryPetitionCancelAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Petition cancel failed.", ct).ConfigureAwait(false);
    }
}
