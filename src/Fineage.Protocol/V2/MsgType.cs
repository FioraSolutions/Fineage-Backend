namespace Fineage.Protocol.V2;

/// <summary>
/// Fineage wire v2. Values 1–4 and 1001–1004 match
/// <c>tools/zone-server</c> and the Unity <c>ZoneV2MsgType</c> enum.
/// Higher values are extensions this server adds; older clients ignore them.
/// </summary>
public enum MsgType : ushort
{
    Hello = 1,
    Auth = 2,
    EnterWorld = 3,
    MoveTo = 4,
    ListCharacters = 5,
    CreateCharacter = 6,
    AuthToken = 7,

    RequestServerList = 10,
    SelectServer = 11,

    HelloAck = 1001,
    AuthOk = 1002,
    PlayerSpawned = 1003,
    MoveToAck = 1004,
    CharacterRoster = 1005,
    CreateCharacterResult = 1006,
    EntityLeft = 1007,
    ServerList = 1010,
    PlayToken = 1011,

    Error = 1099
}
