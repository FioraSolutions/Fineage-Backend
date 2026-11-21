namespace Fineage.Zone.DimensionalRift;

public enum DimensionalRiftEffectKind : byte
{
    SpawnRoom,
    Jump,
    EndInstance,
    EarthquakeWarning
}

public sealed record DimensionalRiftEffect(DimensionalRiftEffectKind Kind, int PartyId);

/// <summary>One party's run through a rift area type (Mobius <c>DimensionalRift</c>).</summary>
public sealed class DimensionalRiftInstance
{
    public required int PartyId { get; init; }
    public required int LeaderObjectId { get; set; }
    public required int Type { get; init; }
    public int CurrentRoom { get; set; }
    public int CurrentJump { get; set; } = 1;
    public int MaxJumps { get; init; } = 4;
    public bool BossRoom { get; set; }
    public long JumpAtUnixMs { get; set; }
    public long SpawnAtUnixMs { get; set; }
    public long EarthquakeAtUnixMs { get; set; }
    public bool Spawned { get; set; }
    public bool WaitingReviveTeleport { get; set; }
    public HashSet<int> CompletedRooms { get; } = [];
    public HashSet<int> DeadMemberIds { get; } = [];
    public HashSet<int> RevivedInWaitingRoom { get; } = [];
    public List<int> SpawnedNpcObjectIds { get; } = [];
    public List<int> MemberSnapshot { get; set; } = [];
}