using Fineage.Net.Host;

namespace Fineage.Zone.Net;

/// <summary>Frames encoded from world state on the simulation thread and sent later without touching the world.</summary>
sealed class PreparedFrames(NetPresenceHub presence)
{
    readonly List<(int ObjectId, ushort Opcode, byte[] Payload, bool Critical)> _frames = [];

    public void Normal(int objectId, ushort opcode, byte[] payload) => _frames.Add((objectId, opcode, payload, false));

    public void Critical(int objectId, ushort opcode, byte[] payload) => _frames.Add((objectId, opcode, payload, true));

    public void Send()
    {
        if (_frames.Count == 0)
            return;

        // One snapshot per connection, in the order the frames were prepared. A spawn burst is not
        // one critical enqueue per NPC frame.
        var order = new List<int>();
        var groups = new Dictionary<int, List<OutboundFrame>>();
        foreach (var (objectId, opcode, payload, _) in _frames)
        {
            if (!groups.TryGetValue(objectId, out List<OutboundFrame>? batch))
            {
                batch = new List<OutboundFrame>();
                groups.Add(objectId, batch);
                order.Add(objectId);
            }

            batch.Add(new OutboundFrame(opcode, payload));
        }

        for (int i = 0; i < order.Count; i++)
            presence.SubmitSnapshot(order[i], groups[order[i]]);
    }
}
