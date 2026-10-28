namespace Fineage.Protocol;

/// <summary>
/// Login TCP lobby on port 9106. The Unity client (<c>FineageLoginSession</c> on <c>dev</c>)
/// speaks this set: opcodes 20–25 and 1020–1026, plus shared <see cref="MsgType.Error"/> 1099.
/// MessagePack keys are appended only. Do not renumber an existing key or opcode.
///
/// <para>Client → server</para>
/// <list type="bullet">
/// <item><description>20 <see cref="MsgType.Register"/> — 0 account, 1 password</description></item>
/// <item><description>21 <see cref="MsgType.CreateCharacter"/> — 0 name, 1 classId, 2 race, 3 sex, 4 hairStyle, 5 hairColor, 6 face.
/// Success is <see cref="MsgType.CharacterCreated"/> then a fresh <see cref="MsgType.CharacterList"/>. Failure is <see cref="MsgType.Error"/>.</description></item>
/// <item><description>22 <see cref="MsgType.ListCharacters"/> — empty body</description></item>
/// <item><description>23 <see cref="MsgType.SelectCharacter"/> — 0 characterId, 1 name, 2 slot.
/// Id is used when it is greater than 0. Otherwise a non-empty name is used.
/// Slot is used only when both id and name are empty. Slot 0 is the first character in creation order.</description></item>
/// <item><description>24 <see cref="MsgType.DeleteCharacter"/> — 0 slot (−1 when deleting by id), 1 characterId (0 when deleting by slot).
/// A positive id wins over the slot.</description></item>
/// <item><description>25 <see cref="MsgType.RestoreCharacter"/> — same keys as delete.</description></item>
/// </list>
///
/// <para>Server → client</para>
/// <list type="bullet">
/// <item><description>1020 <see cref="MsgType.RegisterOk"/></description></item>
/// <item><description>1021 <see cref="MsgType.CharacterCreated"/> — 0 characterId, 1 name, 2 classId, 3 race, 4 sex, 5 hairStyle, 6 hairColor, 7 face, 8 x, 9 y, 10 z. Followed by <see cref="MsgType.CharacterList"/>.</description></item>
/// <item><description>1022 <see cref="MsgType.CharacterList"/> — 0 characters. Sent for list, after a successful delete, and after a successful restore.</description></item>
/// <item><description>1023 <see cref="MsgType.Handoff"/></description></item>
/// <item><description>1024 <see cref="MsgType.LoginOk"/></description></item>
/// <item><description>1025 <see cref="MsgType.CharacterDeleted"/> — 0 slot, 1 characterId, 2 hardDeleted. Always followed by <see cref="MsgType.CharacterList"/>.</description></item>
/// <item><description>1026 <see cref="MsgType.CharacterDeleteFailed"/> — 0 slot, 1 characterId, 2 code, 3 message. The roster is not resent.</description></item>
/// <item><description>1099 <see cref="MsgType.Error"/> — 0 code, 1 message. Also used on the zone port.</description></item>
/// </list>
///
/// <para><see cref="CharacterSummaryMsg"/> keys 0–5 are unchanged. Appended keys:</para>
/// <list type="bullet">
/// <item><description>6 slot (index in creation order)</description></item>
/// <item><description>7 hairStyle, 8 hairColor, 9 face</description></item>
/// <item><description>10 x, 11 y, 12 z (saved position, or the class creation point when none is saved)</description></item>
/// <item><description>13 deleteSecondsLeft (0 when not pending; whole seconds, truncated)</description></item>
/// <item><description>14 isLastPlayed</description></item>
/// </list>
///
/// <para>Readers must tolerate a longer array than they know. Extra elements are additive.</para>
///
/// <para>Error codes (snake_case): <see cref="CharacterNotFound"/>, <see cref="CharacterPendingDelete"/>, <see cref="DeleteFailed"/>,
/// plus the existing <c>auth_required</c>, <c>bad_request</c>, <c>bad_slot</c>, <c>rate_limit</c>.
/// Restore of an unknown slot is ignored: no <see cref="MsgType.Error"/> and no roster.</para>
///
/// <para>A repeated select on the same login session after a handoff is a no-op (no second handoff).
/// Selects are also flood-gated (default 1 second). A gated repeat sends nothing.
/// A character already in the zone is stored and removed from the world when that character enters again.</para>
///
/// <para>V2 lobby replies that used to share ids with zone traffic now live at 1030–1033.
/// See <see cref="V2.MsgType"/>. The Unity client does not send those V2 lobby opcodes;
/// 1005/1006/1007 on the client are <c>CharInfo</c>, <c>NpcInfo</c>, and <c>DeleteObject</c>.</para>
/// </summary>
public static class LobbyProtocol
{
    public static readonly ushort[] ClientToServer =
    [
        (ushort)MsgType.Register,
        (ushort)MsgType.CreateCharacter,
        (ushort)MsgType.ListCharacters,
        (ushort)MsgType.SelectCharacter,
        (ushort)MsgType.DeleteCharacter,
        (ushort)MsgType.RestoreCharacter
    ];

    public static readonly ushort[] ServerToClient =
    [
        (ushort)MsgType.RegisterOk,
        (ushort)MsgType.CharacterCreated,
        (ushort)MsgType.CharacterList,
        (ushort)MsgType.Handoff,
        (ushort)MsgType.LoginOk,
        (ushort)MsgType.CharacterDeleted,
        (ushort)MsgType.CharacterDeleteFailed
    ];

    public const string CharacterNotFound = "character_not_found";
    public const string CharacterPendingDelete = "character_pending_delete";
    public const string DeleteFailed = "delete_failed";

    /// <summary>Empty name or longer than 16 characters (Mobius reason 3).</summary>
    public const string NameTooLong = "name_too_long";

    /// <summary>Charset, forbidden fragment, or name template (Mobius reason 4).</summary>
    public const string InvalidName = "invalid_name";

    /// <summary>Appearance, non-base class, disabled race, or other create rejection (Mobius reason 0).</summary>
    public const string CreationFailed = "creation_failed";

    /// <summary>Server-wide create toggle is off (Mobius reason 5).</summary>
    public const string CreateNotAllowed = "create_not_allowed";

    public const string RaceNotSupported = "race_not_supported";
    public const string ClassNotSupported = "class_not_supported";
    public const string SexNotSupported = "sex_not_supported";
    public const string NameTaken = "name_taken";
    public const string CharacterLimit = "character_limit";

    /// <summary>Zone and inventory opcodes the lobby set must not reuse.</summary>
    public static readonly ushort[] ZoneAndInventory =
    [
        1, 2, 3, 4, 5, 6, 8, 9, 12, 13,
        1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010,
        1012, 1013, 1014, 1015, 1016, 1017, 1018, 1040,
        1099
    ];
}
