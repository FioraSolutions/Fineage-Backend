using Fineage.Net.Host;

namespace Fineage.Zone.Net;

/// <summary>
/// One frame a handler planned on the simulation thread and sends after its reply: to the handler's own connection
/// when <see cref="PeerObjectId"/> is 0, otherwise to that player's connection.
/// </summary>
readonly record struct NetFrame(int PeerObjectId, ushort Opcode, byte[] Payload)
{
    public static NetFrame ToSelf(ushort opcode, byte[] payload) => new(0, opcode, payload);

    /// <summary>
    /// Queues <paramref name="frames"/> without waiting on the mailbox. Frames for this connection are one
    /// ordered batch. A peer that is no longer connected is skipped.
    /// </summary>
    public static Task SendAsync(
        NetConnection conn, NetPresenceHub presence, IEnumerable<NetFrame> frames, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        var self = new List<OutboundFrame>();
        foreach (NetFrame frame in frames)
        {
            if (ct.IsCancellationRequested)
                break;
            if (frame.PeerObjectId == 0)
                self.Add(new OutboundFrame(frame.Opcode, frame.Payload));
            else
                presence.TrySendCritical(frame.PeerObjectId, frame.Opcode, frame.Payload);
        }

        if (self.Count > 0)
            conn.SubmitOrderedBatch(self);
        return Task.CompletedTask;
    }
}
