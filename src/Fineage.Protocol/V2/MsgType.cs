namespace Fineage.Protocol.V2;

/// <summary>
/// Fineage wire v2. Values 1–4 and 1001–1004 match
/// <c>tools/zone-server</c> and the Unity <c>ZoneV2MsgType</c> enum.
/// Higher values are extensions this server adds; older clients ignore them.
/// </summary>
/// <remarks>
/// Lobby replies must not reuse zone ids. <see cref="CharacterRoster"/>, <see cref="CreateCharacterResult"/>,
/// and <see cref="EntityLeft"/> used to be 1005/1006/1007, which are <see cref="CharInfo"/>, <see cref="NpcInfo"/>,
/// and <see cref="DeleteObject"/>. <see cref="ServerList"/> used to be 1010, which is zone <c>StatusUpdate</c>.
/// They now sit at 1030–1033. The Unity client on <c>dev</c> does not send the old lobby ids;
/// it uses <see cref="Fineage.Protocol.MsgType"/> 20–23 / 1020–1024 and treats 1005–1007 as zone traffic.
/// Inventory stays at 1014–1016. Request opcodes 5 and 6 are login-port only and still numerically
/// overlap zone <c>SelectTarget</c> / <c>AttackRequest</c>; the client does not use them.
/// <see cref="EntityLeft"/> is not sent.
/// </remarks>
public enum MsgType : ushort
{
    Hello = 1,
    Auth = 2,
    EnterWorld = 3,
    MoveTo = 4,
    ListCharacters = 5,
    CreateCharacter = 6,
    AuthToken = 7,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.Say"/>.</summary>
    Say = 8,

    RequestServerList = 10,
    SelectServer = 11,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.UseItem"/>.</summary>
    UseItem = 15,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.UnequipItem"/>.</summary>
    UnequipItem = 16,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.GiveItem"/>. Admin grant; off unless the zone enables it.</summary>
    GiveItem = 17,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.PartyInviteRequest"/>.</summary>
    PartyInviteRequest = 18,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.PartyAnswerJoinRequest"/>.</summary>
    PartyAnswerJoinRequest = 19,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.PartyLeaveRequest"/>.</summary>
    PartyLeaveRequest = 26,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.PartyKickRequest"/>.</summary>
    PartyKickRequest = 27,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.AdminSpoilTarget"/>.</summary>
    AdminSpoilTarget = 30,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.AdminSweepCorpse"/>.</summary>
    AdminSweepCorpse = 31,

    HelloAck = 1001,
    AuthOk = 1002,
    PlayerSpawned = 1003,
    MoveToAck = 1004,
    /// <summary>Zone: another player entered knownlist. Same value as <see cref="Fineage.Protocol.MsgType.CharInfo"/>.</summary>
    CharInfo = 1005,
    /// <summary>Zone: shared NPC entered knownlist. Same value as <see cref="Fineage.Protocol.MsgType.NpcInfo"/>.</summary>
    NpcInfo = 1006,
    /// <summary>Zone knownlist remove. Same value as <see cref="Fineage.Protocol.MsgType.DeleteObject"/>.</summary>
    DeleteObject = 1007,
    ServerList = 1033,
    PlayToken = 1011,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.CreatureSay"/>. Zone chat fan-out.</summary>
    CreatureSay = 1012,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.ItemList"/>.</summary>
    ItemList = 1014,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.InventoryUpdate"/>.</summary>
    InventoryUpdate = 1015,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.UserInfo"/>.</summary>
    UserInfo = 1016,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.AbnormalStatusUpdate"/>.</summary>
    AbnormalStatusUpdate = 1017,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.SpawnItem"/>.</summary>
    SpawnItem = 1018,

    /// <summary>Party invite prompt. See <see cref="Fineage.Protocol.MsgType.PartyAskJoin"/>.</summary>
    PartyAskJoin = 1027,
    /// <summary>Invite result for requestor. See <see cref="Fineage.Protocol.MsgType.PartyJoinResult"/>.</summary>
    PartyJoinResult = 1028,
    /// <summary>Full party member snapshot. See <see cref="Fineage.Protocol.MsgType.PartyMemberList"/>.</summary>
    PartyMemberList = 1029,
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.SystemMessage"/>. Chat band 1040–1049.</summary>
    SystemMessage = 1040,

    /// <summary>Login roster. Was 1005; moved so it no longer aliases <see cref="CharInfo"/>.</summary>
    CharacterRoster = 1030,
    /// <summary>Login create result. Was 1006; moved so it no longer aliases <see cref="NpcInfo"/>.</summary>
    CreateCharacterResult = 1031,
    /// <summary>Retired login "slot cleared" notice. Was 1007. The server does not send this.</summary>
    EntityLeft = 1032,

    Error = 1099
}
