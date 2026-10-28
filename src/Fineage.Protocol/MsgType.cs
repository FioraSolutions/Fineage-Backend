namespace Fineage.Protocol;

/// <summary>
/// V2 opcodes. Zone values match Fineage <c>tools/zone-server/Wire/MsgType.cs</c>
/// (including combat ids, which this login process does not implement).
/// Login ids sit in the gaps so a client can speak both without a second numbering scheme.
/// </summary>
public enum MsgType : ushort
{
    // Zone C → S
    Hello = 1,
    Auth = 2,
    EnterWorld = 3,
    MoveTo = 4,
    SelectTarget = 5,
    AttackRequest = 6,
    /// <summary>Dev/playtest buff toggle. Fan-out is <see cref="AbnormalStatusUpdate"/>.</summary>
    BuffRequest = 12,
    /// <summary>Authoritative grant into the player's bag. Reply is <see cref="InventoryUpdate"/>.</summary>
    GrantItemRequest = 13,
    /// <summary>Pick up a ground item by object id. Reply is <see cref="InventoryUpdate"/> and AOI <see cref="DeleteObject"/>.</summary>
    PickupRequest = 14,
    /// <summary>
    /// Equip a bag item, or unequip it when that object is already worn.
    /// Body is <see cref="UseItemMsg"/>. See <see cref="InventoryProtocol"/>.
    /// </summary>
    UseItem = 15,
    /// <summary>
    /// Clear one paperdoll index. Body is <see cref="UnequipItemMsg"/>. See <see cref="InventoryProtocol"/>.
    /// </summary>
    UnequipItem = 16,
    /// <summary>
    /// Debug grant into the bag. Off unless <c>FINEAGE_ZONE_ADMIN</c> is set. Body is <see cref="GiveItemMsg"/>.
    /// </summary>
    GiveItem = 17,
    /// <summary>Invite a player to party. See Project store <c>docs/party-java-l2.md</c>.</summary>
    PartyInviteRequest = 18,
    /// <summary>Accept or decline a pending party invite.</summary>
    PartyAnswerJoinRequest = 19,
    /// <summary>Leave the current party.</summary>
    PartyLeaveRequest = 26,
    /// <summary>Leader removes a member by object id.</summary>
    PartyKickRequest = 27,
    /// <summary>Debug spoil on a living attackable. Off unless <c>FINEAGE_ZONE_ADMIN</c>.</summary>
    AdminSpoilTarget = 30,
    /// <summary>Debug sweeper on a spoiled corpse. Off unless <c>FINEAGE_ZONE_ADMIN</c>.</summary>
    AdminSweepCorpse = 31,
    /// <summary>Create a clan. Body is <see cref="ClanCreateRequestMsg"/>. See <see cref="ClanProtocol"/>.</summary>
    ClanCreateRequest = 32,
    /// <summary>Invite a visible player. Body is <see cref="ClanInviteRequestMsg"/>.</summary>
    ClanInviteRequest = 33,
    /// <summary>Accept or decline a pending clan invite.</summary>
    ClanAnswerJoinRequest = 34,
    /// <summary>Leave the current clan. Empty body. The leader cannot leave.</summary>
    ClanLeaveRequest = 35,
    /// <summary>Leader dismisses a member by object id.</summary>
    ClanKickRequest = 36,
    /// <summary>Leader registers dissolution. Empty body. The clan remains until the dissolve delay elapses.</summary>
    ClanDissolveRequest = 37,
    /// <summary>Leader cancels a pending dissolution. Empty body.</summary>
    ClanCancelDissolveRequest = 38,
    /// <summary>
    /// Select an enchant scroll and open the window. Body is <see cref="EnchantRequestMsg"/>.
    /// Clan uses 32–38. See <see cref="EnchantProtocol"/>.
    /// </summary>
    EnchantRequest = 39,
    /// <summary>Enchant the target with the selected scroll. Body is <see cref="EnchantConfirmMsg"/>.</summary>
    EnchantConfirm = 40,
    /// <summary>Close the enchant window. Body is <see cref="EnchantCancelMsg"/>.</summary>
    EnchantCancel = 41,
    /// <summary>Keepalive from client. Server answers with <see cref="Pong"/>.</summary>
    Ping = 9,
    /// <summary>Zone chat. Fan-out is <see cref="CreatureSay"/>. See <see cref="ChatProtocol"/>.</summary>
    Say = 8,

    // Login C → S. Canonical lobby set; see LobbyProtocol.
    Register = 20,
    CreateCharacter = 21,
    ListCharacters = 22,
    SelectCharacter = 23,
    /// <summary>Mark a roster slot for deletion. See <see cref="LobbyProtocol"/>.</summary>
    DeleteCharacter = 24,
    /// <summary>Clear a pending delete. See <see cref="LobbyProtocol"/>.</summary>
    RestoreCharacter = 25,

    // Zone S → C
    HelloAck = 1001,
    AuthOk = 1002,
    PlayerSpawned = 1003,
    MoveToAck = 1004,
    CharInfo = 1005,
    NpcInfo = 1006,
    DeleteObject = 1007,
    MyTargetSelected = 1008,
    Attack = 1009,
    StatusUpdate = 1010,
    /// <summary>Zone chat fan-out. Not an Interlude opcode. See <see cref="ChatProtocol"/>.</summary>
    CreatureSay = 1012,
    /// <summary>Keepalive reply (echoes <see cref="PingMsg"/> timestamp).</summary>
    Pong = 1013,
    /// <summary>Full inventory on enter world. Same value as <see cref="V2.MsgType.ItemList"/>.</summary>
    ItemList = 1014,
    /// <summary>Add, modify, or remove inventory rows. Same value as <see cref="V2.MsgType.InventoryUpdate"/>.</summary>
    InventoryUpdate = 1015,
    /// <summary>Own character, including paperdoll. Same value as <see cref="V2.MsgType.UserInfo"/>.</summary>
    UserInfo = 1016,
    /// <summary>Active abnormal effects on a creature. Same value as <see cref="V2.MsgType.AbnormalStatusUpdate"/>.</summary>
    AbnormalStatusUpdate = 1017,
    /// <summary>Ground item entered AOI. Keys match Interlude SpawnItem / client <c>SpawnItemDto</c>.</summary>
    SpawnItem = 1018,
    /// <summary>Pending party invite (Interlude AskJoinParty).</summary>
    PartyAskJoin = 1027,
    /// <summary>Invite outcome for the requestor (Interlude JoinParty).</summary>
    PartyJoinResult = 1028,
    /// <summary>Full party roster fan-out (Interlude PartySmallWindowAll snapshot).</summary>
    PartyMemberList = 1029,
    /// <summary>
    /// System line (id, params, plain text). Chat band is 1040–1049.
    /// See <see cref="ChatProtocol"/>. 1041–1049 are reserved.
    /// </summary>
    SystemMessage = 1040,
    /// <summary>Pending clan invite. Clan band is 1050–1059. See <see cref="ClanProtocol"/>.</summary>
    ClanAskJoin = 1050,
    /// <summary>Clan snapshot for online members (Interlude pledge status).</summary>
    PledgeStatusChanged = 1051,
    /// <summary>Full clan member list for online members.</summary>
    PledgeMemberList = 1052,
    /// <summary>Invite outcome for the requestor. 0 declined, any other stored response means accepted.</summary>
    ClanJoinResult = 1053,
    /// <summary>
    /// Enchant roll or cancel. Clan replies use 1050–1059. 1041–1049 stay in the chat band.
    /// See <see cref="EnchantProtocol"/>.
    /// </summary>
    EnchantResult = 1060,

    // Login S → C. Canonical lobby set; see LobbyProtocol.
    RegisterOk = 1020,
    CharacterCreated = 1021,
    CharacterList = 1022,
    Handoff = 1023,
    LoginOk = 1024,
    /// <summary>Delete accepted. A fresh <see cref="CharacterList"/> follows.</summary>
    CharacterDeleted = 1025,
    /// <summary>Delete rejected. The roster is not resent.</summary>
    CharacterDeleteFailed = 1026,

    Error = 1099
}
