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
    public long EndAtUnixMs { get; set; }
    public bool WitchSpawned { get; set; }
}

public enum SevenSignsFestivalEffectKind
{
    EndInstance
}

public readonly record struct SevenSignsFestivalEffect(int PartyId, SevenSignsFestivalEffectKind Kind);