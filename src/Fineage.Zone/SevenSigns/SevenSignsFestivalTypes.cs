namespace Fineage.Zone.SevenSigns;

public sealed class SevenSignsFestivalInstance
{
    public int PartyId { get; init; }
    public int LeaderObjectId { get; init; }
    public int TierId { get; init; }
    public SevenSignsCabal Cabal { get; init; }
    public List<int> MemberSnapshot { get; init; } = [];
    public int InstanceId { get; init; }
    public long OfferingScore { get; set; }
    public long StartedAtUnixMs { get; set; }
    public long EndAtUnixMs { get; set; }
    public bool WitchSpawned { get; set; }
    public bool PrimarySpawned { get; set; }
    public bool SecondarySpawned { get; set; }
    public bool ChestSpawned { get; set; }
    public bool ChallengeIncreased { get; set; }
    public List<int> SpawnedNpcObjectIds { get; init; } = [];
    public int? WitchObjectId { get; set; }
}

public sealed class FestivalPartyRegistration
{
    public int PartyId { get; init; }
    public int LeaderObjectId { get; init; }
    public int TierId { get; init; }
    public SevenSignsCabal Cabal { get; init; }
    public List<int> Members { get; init; } = [];
    public bool Launched { get; set; }
}

public enum SevenSignsFestivalEffectKind
{
    SpawnPrimary,
    SpawnSecondary,
    SpawnChests,
    SpawnWitch,
    SpawnChallenge,
    LaunchRegistration,
    EndInstance
}

public readonly record struct SevenSignsFestivalEffect(
    int PartyId,
    SevenSignsFestivalEffectKind Kind);