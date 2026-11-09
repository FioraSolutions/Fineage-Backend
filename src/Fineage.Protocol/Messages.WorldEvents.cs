namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Opcode <see cref="MsgType.SevenSignsStatusRequest"/> (90). Page is ignored; the reply is the full record.</summary>
public sealed class SevenSignsStatusRequestMsg
{
    public int Page { get; set; } = 1;
}

/// <summary>
/// Opcode <see cref="MsgType.SevenSignsActionRequest"/> (91).
/// <see cref="Action"/> is <see cref="SevenSignsProtocol.ActionJoin"/> or <see cref="SevenSignsProtocol.ActionContribute"/>.
/// </summary>
public sealed class SevenSignsActionRequestMsg
{
    public int Action { get; set; }
    /// <summary>1 Dusk, 2 Dawn. Used by join.</summary>
    public int Cabal { get; set; }
    /// <summary>1 Avarice, 2 Gnosis, 3 Strife. Used by join.</summary>
    public int Seal { get; set; }
    public int BlueStones { get; set; }
    public int GreenStones { get; set; }
    public int RedStones { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsStatus"/> (1090). One record instead of the four Interlude SSQStatus pages.</summary>
public sealed class SevenSignsStatusMsg
{
    public int Period { get; set; }
    public int Cycle { get; set; }
    public int PlayerCabal { get; set; }
    public int PlayerSeal { get; set; }
    public int PlayerStoneCount { get; set; }
    public int PlayerAncientAdena { get; set; }
    public int PlayerContribution { get; set; }
    public int DawnScore { get; set; }
    public int DuskScore { get; set; }
    public int DawnFestivalScore { get; set; }
    public int DuskFestivalScore { get; set; }
    public long DawnStonePoints { get; set; }
    public long DuskStonePoints { get; set; }
    public int AvariceOwner { get; set; }
    public int GnosisOwner { get; set; }
    public int StrifeOwner { get; set; }
    public int DawnMembers { get; set; }
    public int DuskMembers { get; set; }
    public int WinningCabal { get; set; }
    public long NextChangeUnixMs { get; set; }
    public int SkyState { get; set; }
    public int DawnAvarice { get; set; }
    public int DawnGnosis { get; set; }
    public int DawnStrife { get; set; }
    public int DuskAvarice { get; set; }
    public int DuskGnosis { get; set; }
    public int DuskStrife { get; set; }
    public int PreviousWinner { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SevenSignsSky"/> (1091). State 0 normal, 1 dusk, 2 dawn.</summary>
public sealed class SevenSignsSkyMsg
{
    public int State { get; set; }
    public int Period { get; set; }
    public int WinningCabal { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.DungeonEnterRequest"/> (92).
/// <see cref="Action"/> is <see cref="DungeonProtocol.ActionEnter"/> or <see cref="DungeonProtocol.ActionExit"/>.
/// </summary>
public sealed class DungeonEnterRequestMsg
{
    public int DungeonId { get; set; }
    public int Action { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.DungeonEntered"/> (1092).
/// <see cref="Action"/> is enter, exit, or the period-change kick.
/// <see cref="Inside"/> is true only after a successful entrance teleport.
/// </summary>
public sealed class DungeonEnteredMsg
{
    public int DungeonId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public bool Inside { get; set; }
    public int Action { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.SiegeRegisterRequest"/> (46).
/// <see cref="Attacker"/> 1 registers as attacker, 0 as defender.
/// <see cref="Joining"/> 1 registers, 0 withdraws. Mobius <c>RequestJoinSiege</c>.
/// </summary>
public sealed class SiegeRegisterRequestMsg
{
    public int CastleId { get; set; }
    public int Attacker { get; set; }
    public int Joining { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.SiegeDefenderAnswerRequest"/> (47).
/// <see cref="Approved"/> 1 accepts a waiting defender. Any other value refuses. Mobius <c>RequestConfirmSiegeWaitingList</c>.
/// </summary>
public sealed class SiegeDefenderAnswerRequestMsg
{
    public int CastleId { get; set; }
    public int ClanId { get; set; }
    public int Approved { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SiegeListRequest"/> (48).</summary>
public sealed class SiegeListRequestMsg
{
    public int CastleId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.AdminCaptureCastle"/> (49). Off unless <c>FINEAGE_ZONE_ADMIN</c> is set.
/// Calls <c>CaptureCastle(clanId)</c>. Combat does not send this.
/// </summary>
public sealed class AdminCaptureCastleMsg
{
    public int CastleId { get; set; }
    public int ClanId { get; set; }
}

/// <summary>One clan on <see cref="SiegeInfoMsg"/>. Side is owner -1, defender 0, attacker 1, or pending 2.</summary>
public sealed class SiegeClanMsg
{
    public int ClanId { get; set; }
    public int Side { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SiegeInfo"/> (1064). The castle and its current registrations.</summary>
public sealed class SiegeInfoMsg
{
    public int CastleId { get; set; }
    public string Name { get; set; } = "";
    public int OwnerClanId { get; set; }
    public int TaxPercent { get; set; }
    public long SiegeUnixMs { get; set; }
    public long RegistrationDeadlineUnixMs { get; set; }
    public bool RegistrationClosed { get; set; }
    public bool InProgress { get; set; }
    public SiegeClanMsg[] Clans { get; set; } = [];
    /// <summary>Unix milliseconds when the siege ends. 0 when it is not in progress.</summary>
    public long EndsUnixMs { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.BossStatusRequest"/> (93).
/// <see cref="NpcId"/> 0 asks for every scheduled boss.
/// </summary>
public sealed class BossStatusRequestMsg
{
    public int NpcId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.AdminBossSchedule"/> (94).
/// <see cref="Action"/> is <see cref="BossProtocol.ActionMarkDead"/> or <see cref="BossProtocol.ActionForceSpawn"/>.
/// </summary>
public sealed class AdminBossScheduleMsg
{
    public int NpcId { get; set; }
    public int Action { get; set; }
}

/// <summary>Opcode <see cref="MsgType.BossStatus"/> (1093).</summary>
public sealed class BossStatusMsg
{
    public BossStatusEntryMsg[] Bosses { get; set; } = [];
}

/// <summary>One row of <see cref="BossStatusMsg"/>. Keys are 0..10.</summary>
public sealed class BossStatusEntryMsg
{
    public int NpcId { get; set; }
    public int ObjectId { get; set; }
    /// <summary><see cref="BossProtocol.KindRaid"/> or <see cref="BossProtocol.KindGrand"/>.</summary>
    public int Kind { get; set; }
    /// <summary>Alive 0, waiting 1, fighting 2, dead 3. See <see cref="BossProtocol"/>.</summary>
    public int Status { get; set; }
    public long RespawnUnixMs { get; set; }
    public int CurrentHp { get; set; }
    public int MaxHp { get; set; }
    public int CurrentMp { get; set; }
    public int MaxMp { get; set; }
    public bool Enabled { get; set; }
    public bool Spawned { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadStatusRequest"/> (60). Empty body.</summary>
public sealed class OlympiadStatusRequestMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.OlympiadRegisterRequest"/> (61).
/// <see cref="Classed"/> 1 is the class queue. 0 is the non-classed queue.
/// </summary>
public sealed class OlympiadRegisterRequestMsg
{
    public int Classed { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadUnregisterRequest"/> (62). Empty body.</summary>
public sealed class OlympiadUnregisterRequestMsg
{
}

/// <summary>Opcode <see cref="MsgType.OlympiadLeaveRequest"/> (63). Empty body. Leaving a match is a loss.</summary>
public sealed class OlympiadLeaveRequestMsg
{
}

/// <summary>
/// Opcode <see cref="MsgType.AdminReportOlympiadResult"/> (64). Off unless <c>FINEAGE_ZONE_ADMIN</c> is set.
/// <see cref="WinnerObjectId"/> 0 is a draw. Combat calls <c>ReportOlympiadResult</c> with the same pair.
/// </summary>
public sealed class AdminReportOlympiadResultMsg
{
    public int MatchId { get; set; }
    public int WinnerObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadStatus"/> (1070).</summary>
public sealed class OlympiadStatusMsg
{
    public int Period { get; set; }
    public int Cycle { get; set; }
    public bool InCompetition { get; set; }
    public long CompetitionStartUnixMs { get; set; }
    public long CompetitionEndUnixMs { get; set; }
    public long OlympiadEndUnixMs { get; set; }
    public long ValidationEndUnixMs { get; set; }
    public long NextWeeklyUnixMs { get; set; }
    public bool RegistrationOpen { get; set; }
    public bool Noble { get; set; }
    public int Points { get; set; }
    public int CompetitionsDone { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public int Drawn { get; set; }
    /// <summary>0 none, 1 classed queue, 2 non-classed queue.</summary>
    public int Queue { get; set; }
    public int MatchId { get; set; }
    /// <summary>Stadium index, or -1 when this character is not in a match.</summary>
    public int StadiumId { get; set; }
    public int ClassId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadMatch"/> (1071). Sent to both participants.</summary>
public sealed class OlympiadMatchMsg
{
    public int MatchId { get; set; }
    public int StadiumId { get; set; }
    public int ZoneId { get; set; }
    public bool Classed { get; set; }
    /// <summary>0 countdown, 1 pause, 2 battle countdown, 3 fighting, 4 returning.</summary>
    public int Phase { get; set; }
    public int PlayerOneId { get; set; }
    public int PlayerTwoId { get; set; }
    public string PlayerOneName { get; set; } = "";
    public string PlayerTwoName { get; set; } = "";
    public int InstanceId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.OlympiadResult"/> (1072).</summary>
public sealed class OlympiadResultMsg
{
    public int MatchId { get; set; }
    /// <summary>0 tie, 1 player one won, 2 player two won, 3 player one disconnected, 4 player two disconnected, 5 both disconnected, 6 player one defaulted, 7 player two defaulted, 8 both defaulted.</summary>
    public int Outcome { get; set; }
    /// <summary>0 when there is no single winner (tie, both crashed, or a default).</summary>
    public int WinnerObjectId { get; set; }
    public int PointsOne { get; set; }
    public int PointsTwo { get; set; }
    public int DeltaOne { get; set; }
    public int DeltaTwo { get; set; }
    public int RewardItemId { get; set; }
    public int RewardCount { get; set; }
    public int CompetitionsDoneOne { get; set; }
    public int CompetitionsDoneTwo { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.OlympiadCountdown"/> (1073).
/// <see cref="Kind"/> 1 is the stadium teleport, 2 is the fight start, 3 is the return to town.
/// </summary>
public sealed class OlympiadCountdownMsg
{
    public int MatchId { get; set; }
    public int Kind { get; set; }
    public int Seconds { get; set; }
}
