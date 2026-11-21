using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Canonical enter spawn frame ordering for tests and handlers. Presence spawns follow known entity order; peer
/// entrant spawns follow ascending player object id.
/// </summary>
static class NetEnterSpawnRecord
{
    public static void AppendPeerSpawns(
        List<NetFrame> frames,
        ZoneEntity entrant,
        EntityId entrantId,
        byte[] selfRemoteSpawn,
        IReadOnlyList<ZoneEntity> knownPeers,
        ZoneWorld world)
    {
        _ = entrantId;
        foreach (var peer in knownPeers)
        {
            if (peer.Kind != ZoneEntityKind.Player)
                continue;
            frames.Add(new NetFrame(peer.ObjectId, NetOpcode.EntitySpawn, selfRemoteSpawn));
            if (world.SpawnRelationFrame(entrant, peer) is { } relation)
                frames.Add(new NetFrame(peer.ObjectId, NetOpcode.RelationChanged, relation));
        }
    }

    /// <summary>Stable wire signature for enter fanout regression tests (peer, opcode, payload hash).</summary>
    public static IReadOnlyList<string> Describe(IEnumerable<NetFrame> frames)
    {
        var lines = new List<string>();
        foreach (var frame in frames)
        {
            int hash = HashPayload(frame.Payload);
            lines.Add($"{frame.PeerObjectId}:{frame.Opcode}:{hash}");
        }

        return lines;
    }

    static int HashPayload(byte[] payload)
    {
        unchecked
        {
            int hash = 17;
            foreach (byte b in payload)
                hash = hash * 31 + b;
            return hash;
        }
    }
}
