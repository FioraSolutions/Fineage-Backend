using Fineage.Net.Host;

namespace Fineage.Zone.Net;

/// <summary>
/// One frame a handler planned on the simulation thread and sends after its reply: to the handler's own connection
/// when <see cref="PeerObjectId"/> is 0, otherwise to that player's connection.
/// </summary>
readonly record struct NetFrame(int PeerObjectId, ushort Opcode, byte[] Payload)
{
    public static NetFrame ToSelf(ushort opcode, byte[] payload) => new(0, opcode, payload);

    /// <summary>Sends <paramref name="frames"/> in order; a peer that is no longer connected is skipped.</summary>
    public static async Task SendAsync(
        NetConnection conn, NetPresenceHub presence, IEnumerable<NetFrame> frames, CancellationToken ct)
    {
        foreach (var frame in frames)
        {
            if (frame.PeerObjectId == 0)
                await conn.SendAsync(frame.Opcode, frame.Payload, ct: ct).ConfigureAwait(false);
            else
                await presence.SendAsync(frame.PeerObjectId, frame.Opcode, frame.Payload, ct).ConfigureAwait(false);
        }
    }
}
