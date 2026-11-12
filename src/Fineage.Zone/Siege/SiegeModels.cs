using Fineage.Protocol;

namespace Fineage.Zone.Siege;

public enum SiegeStartKind
{
    Started,
    Cancelled,
    AlreadyRunning,
    UnknownCastle
}

public sealed class CastleSiege
{
    internal CastleSiege(CastleTemplate template)
    {
        Id = template.Id;
        Name = template.Name;
        Day = template.Day;
        Hour = template.Hour;
        MaxConcurrent = template.MaxConcurrent;
        TaxPercent = SiegeProtocol.DefaultTaxPercent;
    }

    public int Id { get; }
    public string Name { get; }
    public DayOfWeek Day { get; }
    public int Hour { get; }
    public int MaxConcurrent { get; internal set; }
    public int OwnerClanId { get; internal set; }
    public int TaxPercent { get; internal set; }
    public long SiegeUnixMs { get; internal set; }
    public bool RegistrationClosed { get; internal set; }
    public bool ScheduleAccepted { get; internal set; }
    public bool InProgress { get; internal set; }
    public long EndsUnixMs { get; internal set; }
    internal List<SiegeRegistration> Registrations { get; } = [];
}

public sealed class SiegeRegistration
{
    public SiegeRegistration(int clanId, int side)
    {
        ClanId = clanId;
        Side = side;
    }

    public int ClanId { get; }
    public int Side { get; set; }
}

public readonly record struct SiegeActor(int CharacterId);

public sealed class SiegeClanInfo
{
    public int Id { get; init; }
    public int Level { get; init; }
    public int LeaderId { get; init; }
    public long DissolvingExpiryUnixMs { get; init; }
}

public sealed record SiegeList(
    int CastleId,
    string Name,
    int OwnerClanId,
    int TaxPercent,
    long SiegeUnixMs,
    long RegistrationDeadlineUnixMs,
    bool RegistrationClosed,
    bool InProgress,
    long EndsUnixMs,
    IReadOnlyList<SiegeListClan> Clans);

public readonly record struct SiegeListClan(int ClanId, int Side);
