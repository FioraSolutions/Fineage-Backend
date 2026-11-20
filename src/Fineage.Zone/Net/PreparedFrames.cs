namespace Fineage.Zone.Net;

/// <summary>Frames encoded from world state under the ZoneWorld gate and sent later without touching the world.</summary>
sealed class PreparedFrames(NetPresenceHub presence)
{
    readonly List<(int ObjectId, ushort Opcode, byte[] Payload, bool Critical)> _frames = [];

    public void Normal(int objectId, ushort opcode, byte[] payload) => _frames.Add((objectId, opcode, payload, false));

    public void Critical(int objectId, ushort opcode, byte[] payload) => _frames.Add((objectId, opcode, payload, true));

    public void Send()
    {
        foreach (var (objectId, opcode, payload, critical) in _frames)
        {
            if (critical)
                presence.TrySendCritical(objectId, opcode, payload);
            else
                presence.TrySend(objectId, opcode, payload);
        }
    }
}
