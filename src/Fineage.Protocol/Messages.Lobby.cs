namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

public sealed class HelloMsg
{
    public int ProtocolVersion { get; set; } = 1;
    public string ClientBuild { get; set; } = "";
}

public sealed class HelloAckMsg
{
    public int ProtocolVersion { get; set; } = 1;
    public string ServerName { get; set; } = "FineageZone";
}

/// <summary>
/// Zone login. After character select, the client sends the account here and the handoff token as <see cref="Password"/>.
/// Today's ZoneServer accepts any password, so this payload is enough for it to open a session.
/// </summary>
public sealed class AuthMsg
{
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class AuthOkMsg
{
    public int SessionId { get; set; }
    public string CharacterName { get; set; } = "Test";
}

public sealed class ErrorMsg
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class RegisterMsg
{
    public string Account { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class RegisterOkMsg
{
    public string Account { get; set; } = "";
}

public sealed class LoginOkMsg
{
    public string Account { get; set; } = "";
}

public sealed class CreateCharacterMsg
{
    public string Name { get; set; } = "";
    /// <summary>
    /// Interlude base class. Human 0/10, Elf 18/25, Dark Elf 31/38, Orc 44/49, Dwarf 53.
    /// A non-base class is rejected.
    /// </summary>
    public int ClassId { get; set; }
    /// <summary>0 Human, 1 Elf, 2 Dark Elf, 3 Orc, 4 Dwarf. Must match the class.</summary>
    public int Race { get; set; }
    /// <summary>0 male, 1 female.</summary>
    public int Sex { get; set; }
    /// <summary>0–4 male, 0–6 female. Omitted by older clients (defaults to 0).</summary>
    public int HairStyle { get; set; }
    /// <summary>0–3.</summary>
    public int HairColor { get; set; }
    /// <summary>0–2.</summary>
    public int Face { get; set; }
}

public sealed class CharacterCreatedMsg
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Race { get; set; }
    public int Sex { get; set; }
    public int HairStyle { get; set; }
    public int HairColor { get; set; }
    public int Face { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class CharacterSummaryMsg
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    public int ClassId { get; set; }
    public int Race { get; set; }
    public int Sex { get; set; }
    public int Level { get; set; } = 1;
    /// <summary>Index in creation order. Older clients ignore this key.</summary>
    public int Slot { get; set; }
    public int HairStyle { get; set; }
    public int HairColor { get; set; }
    public int Face { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Whole seconds until hard delete, or 0 when the character is not pending.</summary>
    public int DeleteSecondsLeft { get; set; }
    public bool IsLastPlayed { get; set; }
    /// <summary>CharSelect vitals and progress (Mobius CharSelectionInfo). Older clients ignore these keys.</summary>
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int CurCp { get; set; }
    public int MaxCp { get; set; }
    public long Exp { get; set; }
    public int Sp { get; set; }
    public int Karma { get; set; }
    /// <summary>Right-hand (or two-hand) item id, 0 when unarmed. Same as <see cref="Paperdoll"/>[7].</summary>
    public int WeaponItemId { get; set; }
    /// <summary>
    /// Equipped item ids in paperdoll order, length 17 (Under…Hair2). Index 7 is RHand, 9 gloves, 10 chest, 11 legs, 12 feet.
    /// Zero means empty. Older clients ignore this key.
    /// </summary>
    public int[] Paperdoll { get; set; } = [];
}

public sealed class CharacterListMsg
{
    public CharacterSummaryMsg[] Characters { get; set; } = [];
}

public sealed class SelectCharacterMsg
{
    public int CharacterId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Used when <see cref="CharacterId"/> is 0 and <see cref="Name"/> is empty. −1 means the client did not send a slot. Slot 0 is the first character.</summary>
    public int Slot { get; set; } = -1;
}

/// <summary>Opcode <see cref="MsgType.DeleteCharacter"/> (24). A positive <see cref="CharacterId"/> wins over <see cref="Slot"/>.</summary>
public sealed class DeleteCharacterMsg
{
    /// <summary>−1 when the client is deleting by id.</summary>
    public int Slot { get; set; } = -1;
    /// <summary>0 when the client is deleting by slot.</summary>
    public int CharacterId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.RestoreCharacter"/> (25). Same keys as <see cref="DeleteCharacterMsg"/>.</summary>
public sealed class RestoreCharacterMsg
{
    public int Slot { get; set; } = -1;
    public int CharacterId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterDeleted"/> (1025). A <see cref="CharacterListMsg"/> follows.</summary>
public sealed class CharacterDeletedMsg
{
    public int Slot { get; set; }
    public int CharacterId { get; set; }
    public bool HardDeleted { get; set; }
}

/// <summary>Opcode <see cref="MsgType.CharacterDeleteFailed"/> (1026).</summary>
public sealed class CharacterDeleteFailedMsg
{
    public int Slot { get; set; }
    public int CharacterId { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// Character select result. <see cref="Token"/> is the zone session key (<see cref="AuthMsg.Password"/>).
/// </summary>
public sealed class HandoffMsg
{
    public string Account { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public int CharacterId { get; set; }
    public int ClassId { get; set; }
    public string Token { get; set; } = "";
    public string ZoneHost { get; set; } = "127.0.0.1";
    public int ZonePort { get; set; } = 9777;
    public long ExpiresUnixSeconds { get; set; }
}

public sealed class EnterWorldMsg
{
    public int CharacterSlot { get; set; }
}

/// <summary>Same keys as tools/zone-server <c>PlayerSpawnedMsg</c>.</summary>
public sealed class PlayerSpawnedMsg
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public float RunSpeed { get; set; } = 120f;
    public float WalkSpeed { get; set; } = 80f;
    public bool Running { get; set; } = true;
}

public sealed class PingMsg
{
    public long ClientUnixMs { get; set; }
}

public sealed class PongMsg
{
    public long ClientUnixMs { get; set; }
    public long ServerUnixMs { get; set; }
}
