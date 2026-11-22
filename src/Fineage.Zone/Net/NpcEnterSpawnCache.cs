using System.Collections.Concurrent;
using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.Net;

/// <summary>Shares encoded NPC presence spawns across concurrent joiners when the wire image matches.</summary>
static class NpcEnterSpawnCache
{
    static readonly ConcurrentDictionary<ulong, byte[]> EncodedSpawns = new();

    public static byte[] EncodeNpcSpawn(EntitySpawnMsg spawn)
    {
        if (spawn.Kind != EntityKindNet.Npc)
            return NetCodec.EncodeEntitySpawn(spawn);
        ulong key = HashSpawn(spawn);
        return EncodedSpawns.GetOrAdd(key, _ => NetCodec.EncodeEntitySpawn(spawn));
    }

    static ulong HashSpawn(EntitySpawnMsg spawn)
    {
        unchecked
        {
            ulong h = (ulong)spawn.TemplateId * 1315423911u + (ulong)spawn.X * 2654435761u +
                      (uint)spawn.Y ^ ((ulong)spawn.Z << 16) ^ (ulong)spawn.Heading;
            h = h * 2246822519u + spawn.EntityValue;
            h = h * 3266489917u + spawn.EntityGeneration;
            h ^= spawn.Dead ? 1UL : 0;
            h ^= spawn.Running ? 2UL : 0;
            return h;
        }
    }
}