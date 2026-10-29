using MessagePack;

namespace Fineage.Protocol.V2;

[MessagePackObject]
public sealed partial class HelloMsg
{
    [Key(0)] public int ProtocolVersion { get; set; } = 1;
    [Key(1)] public string ClientBuild { get; set; } = "";
}

[MessagePackObject]
public sealed partial class HelloAckMsg
{
    [Key(0)] public int ProtocolVersion { get; set; } = 1;
    [Key(1)] public string ServerName { get; set; } = "";
}

[MessagePackObject]
public sealed partial class AuthMsg
{
    [Key(0)] public string Account { get; set; } = "";
    [Key(1)] public string Password { get; set; } = "";
}

[MessagePackObject]
public sealed partial class AuthOkMsg
{
    [Key(0)] public int SessionId { get; set; }
    [Key(1)] public string CharacterName { get; set; } = "";
}

[MessagePackObject]
public sealed partial class EnterWorldMsg
{
    [Key(0)] public int CharacterSlot { get; set; }
}

[MessagePackObject]
public sealed partial class PlayerSpawnedMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public float RunSpeed { get; set; } = 120f;
    [Key(6)] public float WalkSpeed { get; set; } = 80f;
    [Key(7)] public bool Running { get; set; } = true;
}

[MessagePackObject]
public sealed partial class MoveToMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int DestX { get; set; }
    [Key(2)] public int DestY { get; set; }
    [Key(3)] public int DestZ { get; set; }
    [Key(4)] public int OriginX { get; set; }
    [Key(5)] public int OriginY { get; set; }
    [Key(6)] public int OriginZ { get; set; }
}

[MessagePackObject]
public sealed partial class ErrorMsg
{
    [Key(0)] public string Code { get; set; } = "";
    [Key(1)] public string Message { get; set; } = "";
}

[MessagePackObject]
public sealed partial class AuthTokenMsg
{
    [Key(0)] public string Account { get; set; } = "";
    [Key(1)] public int PlayKey1 { get; set; }
    [Key(2)] public int PlayKey2 { get; set; }
    [Key(3)] public int LoginKey1 { get; set; }
    [Key(4)] public int LoginKey2 { get; set; }
}

[MessagePackObject]
public sealed partial class SelectServerMsg
{
    [Key(0)] public int ServerId { get; set; } = 1;
}

[MessagePackObject]
public sealed partial class ServerEntryMsg
{
    [Key(0)] public int ServerId { get; set; }
    [Key(1)] public string Host { get; set; } = "127.0.0.1";
    [Key(2)] public int Port { get; set; }
    [Key(3)] public int CurrentPlayers { get; set; }
    [Key(4)] public int MaxPlayers { get; set; }
    [Key(5)] public string Region { get; set; } = "local";
    [Key(6)] public string Name { get; set; } = "";
    [Key(7)] public int Status { get; set; } = 1;
}

[MessagePackObject]
public sealed partial class ServerListMsg
{
    [Key(0)] public ServerEntryMsg[] Servers { get; set; } = [];
}

[MessagePackObject]
public sealed partial class PlayTokenMsg
{
    [Key(0)] public int PlayKey1 { get; set; }
    [Key(1)] public int PlayKey2 { get; set; }
    [Key(2)] public int LoginKey1 { get; set; }
    [Key(3)] public int LoginKey2 { get; set; }
    [Key(4)] public string ZoneHost { get; set; } = "127.0.0.1";
    [Key(5)] public int ZonePort { get; set; }
    [Key(6)] public int ServerId { get; set; }
    [Key(7)] public string Region { get; set; } = "local";
}

[MessagePackObject]
public sealed partial class CreateCharacterMsg
{
    [Key(0)] public string Name { get; set; } = "";
    [Key(1)] public int Race { get; set; }
    [Key(2)] public int Sex { get; set; }
    [Key(3)] public int ClassId { get; set; }
    [Key(4)] public int HairStyle { get; set; }
    [Key(5)] public int HairColor { get; set; }
    [Key(6)] public int Face { get; set; }
}

[MessagePackObject]
public sealed partial class CharacterSummaryMsg
{
    [Key(0)] public int Slot { get; set; }
    [Key(1)] public int ObjectId { get; set; }
    [Key(2)] public string Name { get; set; } = "";
    [Key(3)] public int Race { get; set; }
    [Key(4)] public int Sex { get; set; }
    [Key(5)] public int ClassId { get; set; }
    [Key(6)] public int Level { get; set; }
    [Key(7)] public int X { get; set; }
    [Key(8)] public int Y { get; set; }
    [Key(9)] public int Z { get; set; }
    [Key(10)] public int HairStyle { get; set; }
    [Key(11)] public int HairColor { get; set; }
    [Key(12)] public int Face { get; set; }
    [Key(13)] public int DeleteSecondsLeft { get; set; }
    [Key(14)] public bool IsLastPlayed { get; set; }
    /// <summary>CharSelect vitals and progress (Mobius CharSelectionInfo). Older clients ignore these keys.</summary>
    [Key(15)] public int CurHp { get; set; }
    [Key(16)] public int MaxHp { get; set; }
    [Key(17)] public int CurMp { get; set; }
    [Key(18)] public int MaxMp { get; set; }
    [Key(19)] public int CurCp { get; set; }
    [Key(20)] public int MaxCp { get; set; }
    [Key(21)] public long Exp { get; set; }
    [Key(22)] public int Sp { get; set; }
    [Key(23)] public int Karma { get; set; }
    /// <summary>Right-hand (or two-hand) item id, 0 when unarmed.</summary>
    [Key(24)] public int WeaponItemId { get; set; }
}

[MessagePackObject]
public sealed partial class CharacterRosterMsg
{
    [Key(0)] public CharacterSummaryMsg[] Characters { get; set; } = [];
}

[MessagePackObject]
public sealed partial class CreateCharacterResultMsg
{
    [Key(0)] public bool Ok { get; set; }
    [Key(1)] public string Reason { get; set; } = "";
    [Key(2)] public CharacterSummaryMsg Character { get; set; } = new();
}

[MessagePackObject]
public sealed partial class EntityLeftMsg
{
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.DeleteObjectMsg"/>; zone opcode <see cref="MsgType.DeleteObject"/>.</summary>
[MessagePackObject]
public sealed partial class DeleteObjectMsg
{
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.CharInfoMsg"/>; zone opcode <see cref="MsgType.CharInfo"/>.</summary>
[MessagePackObject]
public sealed partial class CharInfoMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public int ClassId { get; set; }
    /// <summary>Same key as <see cref="Fineage.Protocol.CharInfoMsg.ClanId"/>.</summary>
    [Key(7)] public int ClanId { get; set; }
    /// <summary>Same key as <see cref="Fineage.Protocol.CharInfoMsg.ClanCrestId"/>.</summary>
    [Key(8)] public int ClanCrestId { get; set; }
    /// <summary>Same key as <see cref="Fineage.Protocol.CharInfoMsg.Enchant"/>.</summary>
    [Key(9)] public int[] Enchant { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.NpcInfoMsg"/>; zone opcode <see cref="MsgType.NpcInfo"/>.</summary>
[MessagePackObject]
public sealed partial class NpcInfoMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int NpcId { get; set; }
    [Key(2)] public string Name { get; set; } = "";
    [Key(3)] public int X { get; set; }
    [Key(4)] public int Y { get; set; }
    [Key(5)] public int Z { get; set; }
    [Key(6)] public int Heading { get; set; }
    [Key(7)] public bool Attackable { get; set; } = true;
    [Key(8)] public float RunSpeed { get; set; } = 80f;
    [Key(9)] public float WalkSpeed { get; set; } = 40f;
    /// <summary>Same key as <see cref="Fineage.Protocol.NpcInfoMsg.Title"/>.</summary>
    [Key(10)] public string Title { get; set; } = "";
    /// <summary>Same key as <see cref="Fineage.Protocol.NpcInfoMsg.NpcType"/>.</summary>
    [Key(11)] public string NpcType { get; set; } = "";
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SayMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SayMsg
{
    [Key(0)] public string Text { get; set; } = "";
    [Key(1)] public int ChatType { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.CreatureSayMsg"/>.</summary>
[MessagePackObject]
public sealed partial class CreatureSayMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public string Text { get; set; } = "";
    [Key(3)] public int ChatType { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SystemMessageMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SystemMessageMsg
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string[] Params { get; set; } = [];
    [Key(2)] public string Text { get; set; } = "";
}

/// <summary>Same keys as <see cref="Fineage.Protocol.AbnormalStatusUpdateMsg"/>.</summary>
[MessagePackObject]
public sealed partial class AbnormalStatusUpdateMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public AbnormalEffectMsg[] Effects { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.AbnormalEffectMsg"/>.</summary>
[MessagePackObject]
public sealed partial class AbnormalEffectMsg
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public int Value { get; set; }
    [Key(2)] public int DurationSeconds { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SpawnItemMsg"/>; zone opcode <see cref="MsgType.SpawnItem"/>.</summary>
[MessagePackObject]
public sealed partial class SpawnItemMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ItemId { get; set; }
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public bool Stackable { get; set; }
    [Key(6)] public int Count { get; set; } = 1;
    [Key(7)] public int Unknown { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyInviteRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyInviteRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
    [Key(1)] public int ItemDistribution { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyAnswerJoinRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyAnswerJoinRequestMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public int Response { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyAskJoinMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyAskJoinMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public string RequestorName { get; set; } = "";
    [Key(2)] public int ItemDistribution { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyJoinResultMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyJoinResultMsg
{
    [Key(0)] public int Response { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyMemberEntryMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyMemberEntryMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int Level { get; set; }
    [Key(4)] public int CurHp { get; set; }
    [Key(5)] public int MaxHp { get; set; }
    [Key(6)] public int CurMp { get; set; }
    [Key(7)] public int MaxMp { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyMemberListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyMemberListMsg
{
    [Key(0)] public int LeaderObjectId { get; set; }
    [Key(1)] public int PartyId { get; set; }
    [Key(2)] public PartyMemberEntryMsg[] Members { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyLeaveRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyLeaveRequestMsg
{
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PartyKickRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PartyKickRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanCreateRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanCreateRequestMsg
{
    [Key(0)] public string Name { get; set; } = "";
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanInviteRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanInviteRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
    [Key(1)] public int PledgeType { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanAnswerJoinRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanAnswerJoinRequestMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public int Response { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanLeaveRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanLeaveRequestMsg
{
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanKickRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanKickRequestMsg
{
    [Key(0)] public int TargetObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanDissolveRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanDissolveRequestMsg
{
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanCancelDissolveRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanCancelDissolveRequestMsg
{
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanAskJoinMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanAskJoinMsg
{
    [Key(0)] public int RequestorObjectId { get; set; }
    [Key(1)] public string RequestorName { get; set; } = "";
    [Key(2)] public int ClanId { get; set; }
    [Key(3)] public string ClanName { get; set; } = "";
    [Key(4)] public int PledgeType { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PledgeStatusChangedMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PledgeStatusChangedMsg
{
    [Key(0)] public int ClanId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int Level { get; set; }
    [Key(3)] public int LeaderObjectId { get; set; }
    [Key(4)] public int CrestId { get; set; }
    [Key(5)] public int MemberCount { get; set; }
    [Key(6)] public long DissolvingExpiryUnixMs { get; set; }
    [Key(7)] public long CharPenaltyExpiryUnixMs { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PledgeMemberEntryMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PledgeMemberEntryMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int ClassId { get; set; }
    [Key(3)] public int Level { get; set; }
    [Key(4)] public bool Online { get; set; }
    [Key(5)] public int PledgeType { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.PledgeMemberListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class PledgeMemberListMsg
{
    [Key(0)] public int ClanId { get; set; }
    [Key(1)] public int LeaderObjectId { get; set; }
    [Key(2)] public PledgeMemberEntryMsg[] Members { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ClanJoinResultMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ClanJoinResultMsg
{
    [Key(0)] public int Response { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SevenSignsStatusRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SevenSignsStatusRequestMsg
{
    [Key(0)] public int Page { get; set; } = 1;
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SevenSignsActionRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SevenSignsActionRequestMsg
{
    [Key(0)] public int Action { get; set; }
    [Key(1)] public int Cabal { get; set; }
    [Key(2)] public int Seal { get; set; }
    [Key(3)] public int BlueStones { get; set; }
    [Key(4)] public int GreenStones { get; set; }
    [Key(5)] public int RedStones { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SevenSignsStatusMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SevenSignsStatusMsg
{
    [Key(0)] public int Period { get; set; }
    [Key(1)] public int Cycle { get; set; }
    [Key(2)] public int PlayerCabal { get; set; }
    [Key(3)] public int PlayerSeal { get; set; }
    [Key(4)] public int PlayerStoneCount { get; set; }
    [Key(5)] public int PlayerAncientAdena { get; set; }
    [Key(6)] public int PlayerContribution { get; set; }
    [Key(7)] public int DawnScore { get; set; }
    [Key(8)] public int DuskScore { get; set; }
    [Key(9)] public int DawnFestivalScore { get; set; }
    [Key(10)] public int DuskFestivalScore { get; set; }
    [Key(11)] public long DawnStonePoints { get; set; }
    [Key(12)] public long DuskStonePoints { get; set; }
    [Key(13)] public int AvariceOwner { get; set; }
    [Key(14)] public int GnosisOwner { get; set; }
    [Key(15)] public int StrifeOwner { get; set; }
    [Key(16)] public int DawnMembers { get; set; }
    [Key(17)] public int DuskMembers { get; set; }
    [Key(18)] public int WinningCabal { get; set; }
    [Key(19)] public long NextChangeUnixMs { get; set; }
    [Key(20)] public int SkyState { get; set; }
    [Key(21)] public int DawnAvarice { get; set; }
    [Key(22)] public int DawnGnosis { get; set; }
    [Key(23)] public int DawnStrife { get; set; }
    [Key(24)] public int DuskAvarice { get; set; }
    [Key(25)] public int DuskGnosis { get; set; }
    [Key(26)] public int DuskStrife { get; set; }
    [Key(27)] public int PreviousWinner { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SevenSignsSkyMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SevenSignsSkyMsg
{
    [Key(0)] public int State { get; set; }
    [Key(1)] public int Period { get; set; }
    [Key(2)] public int WinningCabal { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.DungeonEnterRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class DungeonEnterRequestMsg
{
    [Key(0)] public int DungeonId { get; set; }
    [Key(1)] public int Action { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.DungeonEnteredMsg"/>.</summary>
[MessagePackObject]
public sealed partial class DungeonEnteredMsg
{
    [Key(0)] public int DungeonId { get; set; }
    [Key(1)] public int X { get; set; }
    [Key(2)] public int Y { get; set; }
    [Key(3)] public int Z { get; set; }
    [Key(4)] public bool Inside { get; set; }
    [Key(5)] public int Action { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SiegeRegisterRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SiegeRegisterRequestMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public int Attacker { get; set; }
    [Key(2)] public int Joining { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SiegeDefenderAnswerRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SiegeDefenderAnswerRequestMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public int ClanId { get; set; }
    [Key(2)] public int Approved { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SiegeListRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SiegeListRequestMsg
{
    [Key(0)] public int CastleId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.AdminCaptureCastleMsg"/>.</summary>
[MessagePackObject]
public sealed partial class AdminCaptureCastleMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public int ClanId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SiegeClanMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SiegeClanMsg
{
    [Key(0)] public int ClanId { get; set; }
    [Key(1)] public int Side { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SiegeInfoMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SiegeInfoMsg
{
    [Key(0)] public int CastleId { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int OwnerClanId { get; set; }
    [Key(3)] public int TaxPercent { get; set; }
    [Key(4)] public long SiegeUnixMs { get; set; }
    [Key(5)] public long RegistrationDeadlineUnixMs { get; set; }
    [Key(6)] public bool RegistrationClosed { get; set; }
    [Key(7)] public bool InProgress { get; set; }
    [Key(8)] public SiegeClanMsg[] Clans { get; set; } = [];
    [Key(9)] public long EndsUnixMs { get; set; }
}


/// <summary>Same keys as <see cref="Fineage.Protocol.ReviveRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ReviveRequestMsg
{
    [Key(0)] public int Action { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.AdminKillPlayerMsg"/>.</summary>
[MessagePackObject]
public sealed partial class AdminKillPlayerMsg;

/// <summary>Same keys as <see cref="Fineage.Protocol.DieMsg"/>.</summary>
[MessagePackObject]
public sealed partial class DieMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int KillerObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ReviveMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ReviveMsg
{
    [Key(0)] public int ObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.TeleportToLocationMsg"/>.</summary>
[MessagePackObject]
public sealed partial class TeleportToLocationMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int X { get; set; }
    [Key(2)] public int Y { get; set; }
    [Key(3)] public int Z { get; set; }
    [Key(4)] public int Heading { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadStatusRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadStatusRequestMsg
{
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadRegisterRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadRegisterRequestMsg
{
    [Key(0)] public int Classed { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadUnregisterRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadUnregisterRequestMsg
{
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadLeaveRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadLeaveRequestMsg
{
}

/// <summary>Same keys as <see cref="Fineage.Protocol.AdminReportOlympiadResultMsg"/>.</summary>
[MessagePackObject]
public sealed partial class AdminReportOlympiadResultMsg
{
    [Key(0)] public int MatchId { get; set; }
    [Key(1)] public int WinnerObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadStatusMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadStatusMsg
{
    [Key(0)] public int Period { get; set; }
    [Key(1)] public int Cycle { get; set; }
    [Key(2)] public bool InCompetition { get; set; }
    [Key(3)] public long CompetitionStartUnixMs { get; set; }
    [Key(4)] public long CompetitionEndUnixMs { get; set; }
    [Key(5)] public long OlympiadEndUnixMs { get; set; }
    [Key(6)] public long ValidationEndUnixMs { get; set; }
    [Key(7)] public long NextWeeklyUnixMs { get; set; }
    [Key(8)] public bool RegistrationOpen { get; set; }
    [Key(9)] public bool Noble { get; set; }
    [Key(10)] public int Points { get; set; }
    [Key(11)] public int CompetitionsDone { get; set; }
    [Key(12)] public int Won { get; set; }
    [Key(13)] public int Lost { get; set; }
    [Key(14)] public int Drawn { get; set; }
    [Key(15)] public int Queue { get; set; }
    [Key(16)] public int MatchId { get; set; }
    [Key(17)] public int StadiumId { get; set; }
    [Key(18)] public int ClassId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadMatchMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadMatchMsg
{
    [Key(0)] public int MatchId { get; set; }
    [Key(1)] public int StadiumId { get; set; }
    [Key(2)] public int ZoneId { get; set; }
    [Key(3)] public bool Classed { get; set; }
    [Key(4)] public int Phase { get; set; }
    [Key(5)] public int PlayerOneId { get; set; }
    [Key(6)] public int PlayerTwoId { get; set; }
    [Key(7)] public string PlayerOneName { get; set; } = "";
    [Key(8)] public string PlayerTwoName { get; set; } = "";
    [Key(9)] public int InstanceId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadResultMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadResultMsg
{
    [Key(0)] public int MatchId { get; set; }
    [Key(1)] public int Outcome { get; set; }
    [Key(2)] public int WinnerObjectId { get; set; }
    [Key(3)] public int PointsOne { get; set; }
    [Key(4)] public int PointsTwo { get; set; }
    [Key(5)] public int DeltaOne { get; set; }
    [Key(6)] public int DeltaTwo { get; set; }
    [Key(7)] public int RewardItemId { get; set; }
    [Key(8)] public int RewardCount { get; set; }
    [Key(9)] public int CompetitionsDoneOne { get; set; }
    [Key(10)] public int CompetitionsDoneTwo { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.OlympiadCountdownMsg"/>.</summary>
[MessagePackObject]
public sealed partial class OlympiadCountdownMsg
{
    [Key(0)] public int MatchId { get; set; }
    [Key(1)] public int Kind { get; set; }
    [Key(2)] public int Seconds { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.TeleportSelectRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class TeleportSelectRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public string ListName { get; set; } = "";
    [Key(2)] public int LocationId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.WarehouseCountMsg"/>.</summary>
[MessagePackObject]
public sealed partial class WarehouseCountMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int Count { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.WarehouseDepositRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class WarehouseDepositRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.WarehouseWithdrawRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class WarehouseWithdrawRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public WarehouseCountMsg[] Lines { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SupportMagicRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SupportMagicRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.GiveBlessingRequestMsg"/>.</summary>
[MessagePackObject]
public sealed partial class GiveBlessingRequestMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.TeleportLocationMsg"/>.</summary>
[MessagePackObject]
public sealed partial class TeleportLocationMsg
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
    [Key(2)] public int X { get; set; }
    [Key(3)] public int Y { get; set; }
    [Key(4)] public int Z { get; set; }
    [Key(5)] public int FeeItemId { get; set; }
    [Key(6)] public int FeeCount { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.TeleportListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class TeleportListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public string ListName { get; set; } = "";
    [Key(2)] public string ListType { get; set; } = "";
    [Key(3)] public TeleportLocationMsg[] Locations { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.WarehouseDepositListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class WarehouseDepositListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int FeePerLine { get; set; }
    [Key(2)] public int SlotsUsed { get; set; }
    [Key(3)] public int SlotsMax { get; set; }
    [Key(4)] public int Adena { get; set; }
    [Key(5)] public Fineage.Protocol.ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.WarehouseWithdrawListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class WarehouseWithdrawListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int SlotsUsed { get; set; }
    [Key(2)] public int SlotsMax { get; set; }
    [Key(3)] public int InventorySlotsUsed { get; set; }
    [Key(4)] public int InventorySlotsMax { get; set; }
    [Key(5)] public int CurrentWeight { get; set; }
    [Key(6)] public int MaxWeight { get; set; }
    [Key(7)] public Fineage.Protocol.ItemListEntryMsg[] Items { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.NewbieBuffResultMsg"/>.</summary>
[MessagePackObject]
public sealed partial class NewbieBuffResultMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int[] SkillIds { get; set; } = [];
    [Key(2)] public string Refusal { get; set; } = "";
}

/// <summary>Same keys as <see cref="Fineage.Protocol.BlessingResultMsg"/>.</summary>
[MessagePackObject]
public sealed partial class BlessingResultMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int SkillId { get; set; }
    [Key(2)] public bool Accepted { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.RequestBuyItemMsg"/>.</summary>
[MessagePackObject]
public sealed partial class RequestBuyItemMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public ShopCountMsg[] Items { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ShopCountMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ShopCountMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Count { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.RequestSellItemMsg"/>.</summary>
[MessagePackObject]
public sealed partial class RequestSellItemMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public ShopObjectCountMsg[] Items { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ShopObjectCountMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ShopObjectCountMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int Count { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.RequestMultiSellChooseMsg"/>.</summary>
[MessagePackObject]
public sealed partial class RequestMultiSellChooseMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public int EntryId { get; set; }
    [Key(3)] public int Amount { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.RequestPreviewItemMsg"/>.</summary>
[MessagePackObject]
public sealed partial class RequestPreviewItemMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public int ItemId { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.BuyListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class BuyListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public int BaseTaxPercent { get; set; }
    [Key(3)] public int CastleTaxPercent { get; set; }
    [Key(4)] public BuyListItemMsg[] Items { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.BuyListItemMsg"/>.</summary>
[MessagePackObject]
public sealed partial class BuyListItemMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Price { get; set; }
    [Key(2)] public int Count { get; set; } = -1;
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SellListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SellListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public SellListItemMsg[] Items { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.SellListItemMsg"/>.</summary>
[MessagePackObject]
public sealed partial class SellListItemMsg
{
    [Key(0)] public int ObjectId { get; set; }
    [Key(1)] public int ItemId { get; set; }
    [Key(2)] public int Count { get; set; }
    [Key(3)] public int Price { get; set; }
    [Key(4)] public int EnchantLevel { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.MultiSellListMsg"/>.</summary>
[MessagePackObject]
public sealed partial class MultiSellListMsg
{
    [Key(0)] public int NpcObjectId { get; set; }
    [Key(1)] public int ListId { get; set; }
    [Key(2)] public bool MaintainEnchantment { get; set; }
    [Key(3)] public bool InventoryOnly { get; set; }
    [Key(4)] public int CastleTaxPercent { get; set; }
    [Key(5)] public MultiSellEntryMsg[] Entries { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.MultiSellEntryMsg"/>.</summary>
[MessagePackObject]
public sealed partial class MultiSellEntryMsg
{
    [Key(0)] public int EntryId { get; set; }
    [Key(1)] public MultiSellPartMsg[] Ingredients { get; set; } = [];
    [Key(2)] public MultiSellPartMsg[] Products { get; set; } = [];
}

/// <summary>Same keys as <see cref="Fineage.Protocol.MultiSellPartMsg"/>.</summary>
[MessagePackObject]
public sealed partial class MultiSellPartMsg
{
    [Key(0)] public int ItemId { get; set; }
    [Key(1)] public int Count { get; set; }
    [Key(2)] public int EnchantLevel { get; set; }
}

/// <summary>Same keys as <see cref="Fineage.Protocol.ShopBalanceMsg"/>.</summary>
[MessagePackObject]
public sealed partial class ShopBalanceMsg
{
    [Key(0)] public int Adena { get; set; }
    [Key(1)] public int Weight { get; set; }
    [Key(2)] public int MaxWeight { get; set; }
    [Key(3)] public int Slots { get; set; }
    [Key(4)] public int MaxSlots { get; set; }
    [Key(5)] public int BaseTaxPercent { get; set; }
    [Key(6)] public int CastleTaxPercent { get; set; }
}
