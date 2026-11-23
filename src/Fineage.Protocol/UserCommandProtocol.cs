namespace Fineage.Protocol;

/// <summary>Interlude user-command ids (BypassUserCmd) and the system messages those commands send.</summary>
public static class UserCommandProtocol
{
    public const string Unknown = "user_command";
    public const string Refused = "user_command_refused";

    public const int Loc = 0;
    public const int Unstuck = 52;
    public const int Mount = 61;
    public const int Dismount = 62;
    public const int Time = 77;
    public const int PartyInfo = 81;
    public const int ClanWarDeclared = 88;
    public const int ClanWarAgainst = 89;
    public const int ClanWarList = 90;
    public const int ChannelDelete = 93;
    public const int ChannelLeave = 96;
    public const int ChannelInfo = 97;
    public const int SiegeStatus = 99;
    public const int ClanPenalty = 100;
    public const int OlympiadStat = 109;

    public const int TimeDay = 927;
    public const int TimeNight = 928;
    public const int PartyInformation = 1030;
    public const int LootFindersKeepers = 1031;
    public const int LootRandom = 1032;
    public const int LootRandomSpoil = 1033;
    public const int LootByTurn = 1034;
    public const int LootByTurnSpoil = 1035;
    public const int PartyLeader = 1611;
    public const int UnstuckFiveMinutes = 809;
    public const int OlympiadSkillBlocked = 1509;
    public const int OlympiadNotRunning = 1651;
    public const int OlympiadRecord = 1673;
    public const int NoblesseOnly = 1674;
    public const int ChannelDisbanded = 1581;
    public const int ChannelQuit = 1586;
    public const int ChannelPartyLeft = 1587;
    public const int ChannelLeaveLeaderOnly = 1683;
    public const int SiegeStatusNoblesse = 1694;
    public const int NotInClan = 238;
    public const int WarsDeclared = 1571;
    public const int WarsAgainst = 1572;
    public const int WarList = 1612;
    public const int ClanWithAlly = 1200;
    public const int ClanWithoutAlly = 1202;

    public const int PlayerUnstuckMs = 300_000;
    public const int GmUnstuckMs = 1_000;
}
