namespace Fineage.Protocol;

/// <summary>
/// Grand Olympiad registration, matchmaking, and points. Layout: <c>docs/olympiad-v2-protocol.md</c>.
/// </summary>
/// <remarks>
/// Rules and defaults follow L2J Mobius CT 0 Interlude (<c>org.l2jmobius</c>), not its class layout:
/// <c>Olympiad</c>, <c>OlympiadManager</c>, <c>OlympiadGame</c>, <c>OlympiadGameTask</c>,
/// <c>OlympiadStadium</c>, <c>CompetitionType</c>, <c>OlympiadConfig</c>,
/// <c>dist/game/config/Olympiad.ini</c>, and <c>dist/game/data/zones/olympiad_stadium.xml</c>.
/// Hero promotion is not part of this slice.
/// Requests are 60–64. Siege uses 46–49. Replies are 1070–1073. Siege info stays 1064.
/// System lines are 160–179. Siege lines stay 140–149.
/// </remarks>
public static class OlympiadProtocol
{
    public const ushort StatusRequest = 60;
    public const ushort Register = 61;
    public const ushort Unregister = 62;
    public const ushort Leave = 63;
    public const ushort AdminReport = 64;
    public const ushort Status = 1070;
    public const ushort Match = 1071;
    public const ushort Result = 1072;
    public const ushort Countdown = 1073;

    public const int PeriodCompetition = 0;
    public const int PeriodValidation = 1;

    public const int QueueNone = 0;
    public const int QueueClassed = 1;
    public const int QueueNonClassed = 2;

    public const int PhaseCountdown = 0;
    public const int PhasePause = 1;
    public const int PhaseBattleCountdown = 2;
    public const int PhaseFighting = 3;
    public const int PhaseReturning = 4;

    public const int CountdownStadium = 1;
    public const int CountdownFight = 2;
    public const int CountdownReturn = 3;

    public const int OutcomeTie = 0;
    public const int OutcomeWinOne = 1;
    public const int OutcomeWinTwo = 2;
    public const int OutcomeCrashOne = 3;
    public const int OutcomeCrashTwo = 4;
    public const int OutcomeCrashBoth = 5;
    public const int OutcomeDefaultOne = 6;
    public const int OutcomeDefaultTwo = 7;
    public const int OutcomeDefaultBoth = 8;

    /// <summary>No single winner. Draws and cancelled matches use this.</summary>
    public const int NoWinner = 0;

    public const int StartHour = 18;
    public const int StartMinute = 0;
    public const long CompetitionMillis = 21_600_000L;
    public const long BattleMillis = 360_000L;
    public const long WeeklyMillis = 604_800_000L;
    public const long ValidationMillis = 86_400_000L;
    public const int StartPoints = 18;
    public const int WeeklyPoints = 3;
    public const int ClassedMin = 5;
    public const int NonClassedMin = 9;
    public const int RewardItemId = 6651;
    public const int ClassedRewardCount = 50;
    public const int NonClassedRewardCount = 30;
    public const int GatePassesPerPoint = 1000;
    public const int HeroPoints = 100;
    public const int MaxPoints = 10;
    public const int WaitSeconds = 120;
    public const int ClassedPointFloor = 3;
    public const int NonClassedPointFloor = 5;
    public const long RegistrationCloseMillis = 600_000L;
    public const int ArenaPauseMillis = 5_000;
    public const int BattleCountdownMillis = 60_000;
    public const int ReturnCountdownMillis = 40_000;
    public const int SpawnSeparation = 900;
    public const int MatchmakeMillis = 30_000;
    public const int IdleMatchmakeMillis = 60_000;
    public const int InventoryLimit = 80;
    public const int DwarfInventoryLimit = 100;
    public const int DwarfRace = 4;
    public const int Zariche = 8190;
    public const int Akamanah = 8689;

    public const string Disabled = "olympiad_disabled";
    public const string Period = "olympiad_period";
    public const string Noble = "olympiad_noble";
    public const string Subclass = "olympiad_subclass";
    public const string Cursed = "olympiad_cursed";
    public const string Inventory = "olympiad_inventory";
    public const string Late = "olympiad_late";
    public const string Classed = "olympiad_classed";
    public const string Registered = "olympiad_registered";
    public const string Points = "olympiad_points";
    public const string Selected = "olympiad_selected";
    public const string None = "olympiad_none";
    public const string MatchMissing = "olympiad_match";
    public const string Phase = "olympiad_phase";
    public const string Winner = "olympiad_winner";
    public const string Store = "olympiad_store";

    public const int NotInProgress = 160;
    public const int NoblesOnly = 161;
    public const int SubclassBlocked = 162;
    public const int CursedWeapon = 163;
    public const int InventoryBlocked = 164;
    public const int TooLate = 165;
    public const int AlreadyClassed = 166;
    public const int AlreadyRegistered = 167;
    public const int RegisteredClassed = 168;
    public const int RegisteredNonClassed = 169;
    public const int Unregistered = 170;
    public const int AlreadySelected = 171;
    public const int NotRegistered = 172;
    public const int NeedPoints = 173;
    public const int CompetitionStarted = 174;
    public const int CompetitionEnded = 175;
    public const int RegistrationEnded = 176;
    public const int CountdownLine = 177;
    public const int PointsChanged = 178;
    public const int MatchDecided = 179;
}
