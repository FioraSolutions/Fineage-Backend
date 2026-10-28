namespace Fineage.Zone.SevenSigns;

/// <summary>0 none, 1 dusk, 2 dawn. Same numbers the Interlude record uses.</summary>
public enum SevenSignsCabal
{
    None = 0,
    Dusk = 1,
    Dawn = 2
}

/// <summary>0 none, 1 avarice, 2 gnosis, 3 strife.</summary>
public enum SevenSignsSeal
{
    None = 0,
    Avarice = 1,
    Gnosis = 2,
    Strife = 3
}

/// <summary>Recruitment, competition, results, seal validation.</summary>
public enum SevenSignsPeriod
{
    Recruiting = 0,
    Competition = 1,
    Results = 2,
    SealValidation = 3
}

/// <summary>Strife combat standing during seal validation. Query only; no skill is applied.</summary>
public enum SevenSignsStrifeRole
{
    None = 0,
    Victor = 1,
    Vanquished = 2
}

public interface IGameClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemGameClock : IGameClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class MutableGameClock : IGameClock
{
    public MutableGameClock(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; set; }
}

/// <summary>
/// Festival of Darkness lifecycle. Score changes go through <see cref="SevenSignsEngine.AddFestivalScore"/>.
/// The festival instance, rooms, and rewards are out of scope.
/// </summary>
public interface ISevenSignsFestivalHook
{
    void OnCompetitionStarted();

    /// <summary>Called with the winner taken before this hook runs, so a score change here can still move seal ownership.</summary>
    void OnCompetitionEnded(SevenSignsCabal winnerBeforeReward);

    void OnCycleReset();
}

public readonly record struct SevenSignsNotice(int Id, string Text);

public readonly record struct SevenSignsJoinCost(int Adena, int CertificateCount);

/// <summary>Pays a Dawn join cost. Return false to refuse the join without changing cabal state.</summary>
public delegate bool SevenSignsPayer(SevenSignsJoinCost cost);

/// <summary>Defaults follow L2J Mobius Interlude <c>Feature.ini</c> Seven Signs keys and the Monday 18:00 period.</summary>
public sealed class SevenSignsOptions
{
    public const int DefaultMaxContribution = 1_000_000;
    public const int DefaultDawnJoinFee = 50_000;
    public const int DefaultClaimPercent = 35;
    public const int DefaultRetainPercent = 10;
    public const int DefaultStoneScoreScale = 500;

    public DayOfWeek PeriodStartDay { get; init; } = DayOfWeek.Monday;

    public int PeriodStartHour { get; init; } = 18;

    public int PeriodStartMinute { get; init; } = 0;

    public TimeSpan MinorLength { get; init; } = TimeSpan.FromMinutes(15);

    public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

    public int MaxPlayerContribution { get; init; } = DefaultMaxContribution;

    public int DawnJoinFeeAdena { get; init; } = DefaultDawnJoinFee;

    public int CertificateItemId { get; init; } = Fineage.Protocol.SevenSignsProtocol.CertificateOfApprovalId;

    /// <summary><c>AltCastleForDawn</c>. After the second class transfer, Dawn costs a castle, a certificate, or adena.</summary>
    public bool CastleRestrictsDawn { get; init; } = true;

    /// <summary><c>AltCastleForDusk</c>. A castle-owning clan cannot join Dusk.</summary>
    public bool CastleRestrictsDusk { get; init; } = true;

    public int ClaimPercent { get; init; } = DefaultClaimPercent;

    public int RetainPercent { get; init; } = DefaultRetainPercent;

    public int StoneScoreScale { get; init; } = DefaultStoneScoreScale;

    public double DawnGatePhysical { get; init; } = 1.1;

    public double DuskGatePhysical { get; init; } = 0.8;

    public double DawnGateMagic { get; init; } = 1.1;

    public double DuskGateMagic { get; init; } = 0.8;

    /// <summary>
    /// Mobius <c>AltSevenSignsLazyUpdate</c> defaults to true (write on period change and logout).
    /// When false, each accepted join, contribution, and festival score is written immediately.
    /// </summary>
    public bool LazyUpdate { get; init; } = true;

    public static SevenSignsOptions FromEnvironment()
    {
        string? zoneId = Environment.GetEnvironmentVariable("FINEAGE_SERVER_TIMEZONE");
        TimeZoneInfo zone = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim());
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

        return new SevenSignsOptions
        {
            TimeZone = zone,
            PeriodStartHour = ReadInt("FINEAGE_SSQ_START_HOUR", 18, 0, 23),
            PeriodStartMinute = ReadInt("FINEAGE_SSQ_START_MINUTE", 0, 0, 59),
            MinorLength = TimeSpan.FromMinutes(ReadInt("FINEAGE_SSQ_MINOR_MINUTES", 15, 1, 24 * 60)),
            PeriodStartDay = ReadDay(Environment.GetEnvironmentVariable("FINEAGE_SSQ_START_DAY")),
            LazyUpdate = ReadBool("FINEAGE_SSQ_LAZY_UPDATE", true),
            MaxPlayerContribution = ReadInt("FINEAGE_SSQ_MAX_CONTRIB", DefaultMaxContribution, 1, int.MaxValue),
            DawnJoinFeeAdena = ReadInt("FINEAGE_SSQ_DAWN_FEE", DefaultDawnJoinFee, 0, int.MaxValue)
        };
    }

    static DayOfWeek ReadDay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DayOfWeek.Monday;
        if (Enum.TryParse<DayOfWeek>(text.Trim(), ignoreCase: true, out var day))
            return day;
        throw new InvalidOperationException($"FINEAGE_SSQ_START_DAY '{text}' is not a weekday name.");
    }

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }

    static bool ReadBool(string name, bool fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        if (text.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Trim().Equals("0", StringComparison.OrdinalIgnoreCase) ||
            text.Trim().Equals("false", StringComparison.OrdinalIgnoreCase) ||
            text.Trim().Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;
        return fallback;
    }
}
