using Fineage.Accounts;

namespace Fineage.Zone.Clan;

public enum ChSiegeStartKind
{
    Started,
    Cancelled,
    AlreadyRunning,
    UnknownHall
}

public sealed class ChSiegeHall
{
    internal ChSiegeHall(SiegableHallTemplate template)
    {
        Id = template.Id;
        Name = template.Name;
        SiegeLengthMs = template.SiegeLengthMs;
        SchedulePeriodDays = template.SchedulePeriodDays;
        ScheduleHour = template.ScheduleHour;
        ScheduleMinute = template.ScheduleMinute;
        AllowStartWithoutAttackers = template.AllowStartWithoutAttackers;
    }

    public int Id { get; }
    public string Name { get; }
    public long SiegeLengthMs { get; }
    public int SchedulePeriodDays { get; }
    public int ScheduleHour { get; }
    public int ScheduleMinute { get; }
    public bool AllowStartWithoutAttackers { get; }

    public int OwnerClanId { get; internal set; }
    public long NextSiegeUnixMs { get; internal set; }
    public bool RegistrationClosed { get; internal set; }
    public bool InProgress { get; internal set; }
    public long EndsUnixMs { get; internal set; }
    public int PendingWinnerClanId { get; internal set; }
    public List<int> Attackers { get; } = [];
}