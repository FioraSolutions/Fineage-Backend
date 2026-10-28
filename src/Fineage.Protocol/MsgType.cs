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
    /// <summary>Keepalive from client. Server answers with <see cref="Pong"/>.</summary>
    Ping = 9,
    /// <summary>Zone chat. Fan-out is <see cref="CreatureSay"/>.</summary>
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
    /// <summary>Zone chat fan-out. Not an Interlude opcode.</summary>
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
