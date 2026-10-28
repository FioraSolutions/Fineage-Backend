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
