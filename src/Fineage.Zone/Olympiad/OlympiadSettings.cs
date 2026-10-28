using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

/// <summary>Mobius <c>Olympiad.ini</c> values. Invalid environment overrides keep the default.</summary>
public sealed class OlympiadSettings
{
    public bool Enabled { get; init; } = true;
    public int StartHour { get; init; } = OlympiadProtocol.StartHour;
    public int StartMinute { get; init; } = OlympiadProtocol.StartMinute;
    public long CompetitionMillis { get; init; } = OlympiadProtocol.CompetitionMillis;
    public long BattleMillis { get; init; } = OlympiadProtocol.BattleMillis;
    public long WeeklyMillis { get; init; } = OlympiadProtocol.WeeklyMillis;
    public long ValidationMillis { get; init; } = OlympiadProtocol.ValidationMillis;
    public int StartPoints { get; init; } = OlympiadProtocol.StartPoints;
    public int WeeklyPoints { get; init; } = OlympiadProtocol.WeeklyPoints;
    public int ClassedMin { get; init; } = OlympiadProtocol.ClassedMin;
    public int NonClassedMin { get; init; } = OlympiadProtocol.NonClassedMin;
    public int RewardItemId { get; init; } = OlympiadProtocol.RewardItemId;
    public int ClassedRewardCount { get; init; } = OlympiadProtocol.ClassedRewardCount;
    public int NonClassedRewardCount { get; init; } = OlympiadProtocol.NonClassedRewardCount;
    public int MaxPoints { get; init; } = OlympiadProtocol.MaxPoints;
    public int WaitSeconds { get; init; } = OlympiadProtocol.WaitSeconds;
    public int ClassedPointFloor { get; init; } = OlympiadProtocol.ClassedPointFloor;
    public int NonClassedPointFloor { get; init; } = OlympiadProtocol.NonClassedPointFloor;
    public long RegistrationCloseMillis { get; init; } = OlympiadProtocol.RegistrationCloseMillis;
    public bool UseCustomPeriod { get; init; }
    public string PeriodKind { get; init; } = "MONTH";
    public int PeriodMultiplier { get; init; } = 1;
    public IReadOnlyList<int> CompetitionDays { get; init; } = [1, 2, 3, 4, 5, 6, 7];
    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    public static OlympiadSettings Mobius { get; } = new();

    public static OlympiadSettings FromEnvironment()
    {
        int wait = ReadInt("FINEAGE_OLY_WAIT_SECONDS", OlympiadProtocol.WaitSeconds, 1, 3_600);
        if (wait is not (120 or 60 or 30 or 15 or 5))
            wait = OlympiadProtocol.WaitSeconds;

        return new OlympiadSettings
        {
            Enabled = ReadBool("FINEAGE_OLY_ENABLED", true),
            StartHour = ReadInt("FINEAGE_OLY_START_HOUR", OlympiadProtocol.StartHour, 0, 23),
            StartMinute = ReadInt("FINEAGE_OLY_START_MINUTE", OlympiadProtocol.StartMinute, 0, 59),
            CompetitionMillis = ReadLong("FINEAGE_OLY_COMPETITION_MS", OlympiadProtocol.CompetitionMillis),
            BattleMillis = ReadLong("FINEAGE_OLY_BATTLE_MS", OlympiadProtocol.BattleMillis),
            WeeklyMillis = ReadLong("FINEAGE_OLY_WEEKLY_MS", OlympiadProtocol.WeeklyMillis),
            ValidationMillis = ReadLong("FINEAGE_OLY_VALIDATION_MS", OlympiadProtocol.ValidationMillis),
            StartPoints = ReadInt("FINEAGE_OLY_START_POINTS", OlympiadProtocol.StartPoints, 0, 1_000_000),
            WeeklyPoints = ReadInt("FINEAGE_OLY_WEEKLY_POINTS", OlympiadProtocol.WeeklyPoints, 0, 1_000_000),
            ClassedMin = ReadInt("FINEAGE_OLY_CLASSED_MIN", OlympiadProtocol.ClassedMin, 2, 100),
            NonClassedMin = ReadInt("FINEAGE_OLY_NONCLASSED_MIN", OlympiadProtocol.NonClassedMin, 2, 100),
            RewardItemId = ReadInt("FINEAGE_OLY_REWARD_ITEM", OlympiadProtocol.RewardItemId, 1, int.MaxValue),
            ClassedRewardCount = ReadInt("FINEAGE_OLY_CLASSED_REWARD", OlympiadProtocol.ClassedRewardCount, 0, 100_000),
            NonClassedRewardCount = ReadInt("FINEAGE_OLY_NONCLASSED_REWARD", OlympiadProtocol.NonClassedRewardCount, 0, 100_000),
            MaxPoints = ReadInt("FINEAGE_OLY_MAX_POINTS", OlympiadProtocol.MaxPoints, 1, 1_000_000),
            WaitSeconds = wait,
            UseCustomPeriod = ReadBool("FINEAGE_OLY_CUSTOM_PERIOD", false),
            PeriodKind = ReadPeriod(Environment.GetEnvironmentVariable("FINEAGE_OLY_PERIOD")),
            PeriodMultiplier = ReadInt("FINEAGE_OLY_PERIOD_MULTIPLIER", 1, 1, 12),
            CompetitionDays = ReadDays(Environment.GetEnvironmentVariable("FINEAGE_OLY_COMPETITION_DAYS")),
            TimeZone = ReadZone()
        };
    }

    static TimeZoneInfo ReadZone()
    {
        string? zoneId = Environment.GetEnvironmentVariable("FINEAGE_SERVER_TIMEZONE");
        if (string.IsNullOrWhiteSpace(zoneId))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim());
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"FINEAGE_SERVER_TIMEZONE '{zoneId}' is not a known time zone.", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new InvalidOperationException($"FINEAGE_SERVER_TIMEZONE '{zoneId}' is not a valid time zone.", ex);
        }
    }

    static string ReadPeriod(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "MONTH";
        string kind = text.Trim().ToUpperInvariant();
        return kind is "DAY" or "WEEK" or "MONTH" ? kind : "MONTH";
    }

    static IReadOnlyList<int> ReadDays(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [1, 2, 3, 4, 5, 6, 7];
        var days = new List<int>();
        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out int day) && day is >= 1 and <= 7 && !days.Contains(day))
                days.Add(day);
        }

        return days.Count == 0 ? [1, 2, 3, 4, 5, 6, 7] : days;
    }

    static bool ReadBool(string name, bool fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (text is "1" or "true" or "yes" or "on")
            return true;
        if (text is "0" or "false" or "no" or "off")
            return false;
        return fallback;
    }

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    static long ReadLong(string name, long fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !long.TryParse(text, out long value) || value < 1)
            return fallback;
        return value;
    }
}
