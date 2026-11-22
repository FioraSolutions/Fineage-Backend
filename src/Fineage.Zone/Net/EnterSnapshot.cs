using Fineage.Net.Contract;
using Fineage.Protocol;
using EntitySpawnMsg = Fineage.Net.Contract.EntitySpawnMsg;

namespace Fineage.Zone.Net;

/// <summary>Immutable enter burst plan produced on the simulation thread; encoding runs off-thread.</summary>
sealed class EnterSnapshot
{
    internal static EnterSnapshot ForTestEnter() => new()
    {
        SessionObjectId = 0,
        Self = [],
        SelfRemoteSpawn = new EntitySpawnMsg(),
        Known = [],
        PeerFanout = []
    };

    public int SessionObjectId { get; init; }
    public required IReadOnlyList<EnterSelfSlot> Self { get; init; }
    public required EntitySpawnMsg SelfRemoteSpawn { get; init; }
    public required IReadOnlyList<KnownPresenceSlot> Known { get; init; }
    public required IReadOnlyList<PeerFanoutSlot> PeerFanout { get; init; }
    public int KnownEntityCount { get; init; }
    public int KnownPlayerCount { get; init; }
}

/// <summary>One self-connection frame before NetCodec (or pre-built wire bytes for relation-only slots).</summary>
readonly record struct EnterSelfSlot(ushort Opcode, object? Message, byte[]? Payload = null);

/// <summary>One entity on the joiner's knownlist at commit time.</summary>
sealed class KnownPresenceSlot
{
    public int SourceObjectId { get; init; }
    public bool GroundItem { get; init; }
    public DropItemNetMsg? Drop { get; init; }
    public EntitySpawnMsg? Spawn { get; init; }
    public EntityKindNet Kind { get; init; }
    public IReadOnlyList<PresenceWireSlot> FollowUps { get; init; } = [];
    public byte[]? RelationToEntrant { get; init; }
}

readonly record struct PresenceWireSlot(ushort Opcode, byte[] Payload);

readonly record struct PeerFanoutSlot(int PeerObjectId, byte[]? RelationPayload);