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
    /// <summary>Same value as <see cref="Fineage.Protocol.MsgType.Say"/>.</summary>
    Say = 8,

    RequestServerList = 10,
    SelectServer = 11,

    HelloAck = 1001,
    AuthOk = 1002,
    PlayerSpawned = 1003,
    MoveToAck = 1004,
    CharacterRoster = 1005,
    CreateCharacterResult = 1006,
    /// <summary>Login roster slot cleared. On the zone port the same value is AOI remove.</summary>
    EntityLeft = 1007,
    /// <summary>Zone knownlist remove; same as <see cref="Fineage.Protocol.MsgType.DeleteObject"/>.</summary>
    DeleteObject = EntityLeft,
    /// <summary>Zone: another player entered AOI; same value as <see cref="Fineage.Protocol.MsgType.CharInfo"/>.</summary>
    CharInfo = CharacterRoster,
    /// <summary>Zone: shared NPC entered AOI; same value as <see cref="Fineage.Protocol.MsgType.NpcInfo"/>.</summary>
    NpcInfo = CreateCharacterResult,
    ServerList = 1010,
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

    Error = 1099
}
