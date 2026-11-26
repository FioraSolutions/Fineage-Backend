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
        SelfRemoteFollowUps = [],
        Known = [],
        PeerFanout = []
    };

    public int SessionObjectId { get; init; }
    public required IReadOnlyList<EnterSelfSlot> Self { get; init; }
    public required EntitySpawnMsg SelfRemoteSpawn { get; init; }
    /// <summary>The entrant's live presence state, sent to existing peers just as it is on the later move diff path.</summary>
    public required IReadOnlyList<PresenceWireSlot> SelfRemoteFollowUps { get; init; }
    public required IReadOnlyList<KnownPresenceSlot> Known { get; init; }
    public required IReadOnlyList<PeerFanoutSlot> PeerFanout { get; init; }
    public int KnownEntityCount { get; init; }
    public int KnownPlayerCount { get; init; }
}

/// <summary>One self-connection frame before NetCodec (or pre-built wire bytes for relation-only slots).</summary>
readonly record struct EnterSelfSlot(ushort Opcode, object? Message, byte[]? Payload = null);

/// <summary>
/// Self frames built off the simulation thread, plus the body fields they were built from.
/// CommitEnter rebuilds them only when world admission changed one of those fields.
/// </summary>
sealed class EnterProjection
{
    public required IReadOnlyList<EnterSelfSlot> Slots { get; init; }
    public required EntitySpawnMsg Remote { get; init; }
    public required int SkillCount { get; init; }
    public required bool Hero { get; init; }
    public required bool Noble { get; init; }
    public required int ClanId { get; init; }
    public required int ClanCrestId { get; init; }
    public required int PledgeClass { get; init; }
    public required int PartnerId { get; init; }
    public required bool Married { get; init; }
    public required bool DangerArea { get; init; }
    public required float RunSpeed { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Z { get; init; }
}

/// <summary>One entity on the joiner's knownlist at commit time.</summary>
sealed class KnownPresenceSlot
{
    public int SourceObjectId { get; init; }
    public bool GroundItem { get; init; }
    public DropItemNetMsg? Drop { get; init; }
    public EntitySpawnMsg? Spawn { get; init; }
    public NpcPresenceCapture? Npc { get; init; }
    public EntityKindNet Kind { get; init; }
    public IReadOnlyList<PresenceWireSlot> FollowUps { get; init; } = [];
    public byte[]? RelationToEntrant { get; init; }
}

readonly record struct PresenceWireSlot(ushort Opcode, object? Message, byte[]? Payload = null);

readonly record struct PeerFanoutSlot(int PeerObjectId, byte[]? RelationPayload);