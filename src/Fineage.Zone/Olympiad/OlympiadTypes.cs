using Fineage.Protocol;

namespace Fineage.Zone.Olympiad;

public enum OlympiadMatchPhase
{
    Countdown = OlympiadProtocol.PhaseCountdown,
    Pause = OlympiadProtocol.PhasePause,
    BattleCountdown = OlympiadProtocol.PhaseBattleCountdown,
    Fighting = OlympiadProtocol.PhaseFighting,
    Returning = OlympiadProtocol.PhaseReturning,
    Done = 5
}

public readonly record struct OlympiadActor(
    int CharacterId,
    string Name,
    int ClassId,
    int BaseClassId,
    int InventorySlots,
    int InventoryLimit,
    bool CursedWeapon);

public readonly record struct OlympiadPresence(
    bool Online,
    bool Dead,
    int ClassId,
    int BaseClassId,
    int X,
    int Y,
    int Z,
    int InventorySlots,
    int InventoryLimit,
    bool CursedWeapon);

public readonly record struct OlympiadMove(int CharacterId, int X, int Y, int Z, int InstanceId, bool Persist);

public readonly record struct OlympiadGrant(int CharacterId, int ItemId, int Count);

public readonly record struct OlympiadNotice(int CharacterId, int MessageId, string Text, string[] Params);

public readonly record struct OlympiadCountdownNote(int MatchId, int Kind, int Seconds, int PlayerOneId, int PlayerTwoId);

public sealed class OlympiadResultNote
{
    public int MatchId { get; init; }
    public int Outcome { get; init; }
    public int WinnerObjectId { get; init; }
    public int PlayerOneId { get; init; }
    public int PlayerTwoId { get; init; }
    public int PointsOne { get; init; }
    public int PointsTwo { get; init; }
    public int DeltaOne { get; init; }
    public int DeltaTwo { get; init; }
    public int RewardItemId { get; init; }
    public int RewardCount { get; init; }
    public int CompetitionsDoneOne { get; init; }
    public int CompetitionsDoneTwo { get; init; }
    public string PlayerOneName { get; init; } = "";
    public string PlayerTwoName { get; init; } = "";
}

public sealed class OlympiadMatchNote
{
    public int MatchId { get; init; }
    public int StadiumId { get; init; }
    public int ZoneId { get; init; }
    public bool Classed { get; init; }
    public int Phase { get; init; }
    public int PlayerOneId { get; init; }
    public int PlayerTwoId { get; init; }
    public string PlayerOneName { get; init; } = "";
    public string PlayerTwoName { get; init; } = "";
    public int InstanceId { get; init; }
}

public sealed class OlympiadPersonal
{
    public int Period { get; init; }
    public int Cycle { get; init; }
    public bool InCompetition { get; init; }
    public long CompetitionStartUnixMs { get; init; }
    public long CompetitionEndUnixMs { get; init; }
    public long OlympiadEndUnixMs { get; init; }
    public long ValidationEndUnixMs { get; init; }
    public long NextWeeklyUnixMs { get; init; }
    public bool RegistrationOpen { get; init; }
    public bool Noble { get; init; }
    public int Points { get; init; }
    public int CompetitionsDone { get; init; }
    public int Won { get; init; }
    public int Lost { get; init; }
    public int Drawn { get; init; }
    public int Queue { get; init; }
    public int MatchId { get; init; }
    public int StadiumId { get; init; }
    public int ClassId { get; init; }
}

public sealed class OlympiadStep
{
    public string? Error { get; set; }
    public List<OlympiadNotice> Notices { get; } = [];
    public List<OlympiadMove> Moves { get; } = [];
    public List<OlympiadGrant> Grants { get; } = [];
    public List<OlympiadMatchNote> Matches { get; } = [];
    public List<OlympiadResultNote> Results { get; } = [];
    public List<OlympiadCountdownNote> Countdowns { get; } = [];
    public List<int> RefreshStatus { get; } = [];
    public bool RefreshAll { get; set; }
}
