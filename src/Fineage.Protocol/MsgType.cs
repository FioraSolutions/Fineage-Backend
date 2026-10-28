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
    /// <summary>Keepalive from client. Server answers with <see cref="Pong"/>.</summary>
    Ping = 9,
    /// <summary>Zone chat. Fan-out is <see cref="CreatureSay"/>.</summary>
    Say = 8,

    // Login C → S
    Register = 20,
    CreateCharacter = 21,
    ListCharacters = 22,
    SelectCharacter = 23,

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

    // Login S → C
    RegisterOk = 1020,
    CharacterCreated = 1021,
    CharacterList = 1022,
    Handoff = 1023,
    LoginOk = 1024,

    Error = 1099
}
