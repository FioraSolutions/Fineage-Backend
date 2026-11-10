using Fineage.Game.Quests;

namespace Fineage.Accounts;

public enum CreateCharacterStatus
{
    Ok,
    AccountMissing,
    NameTaken,
    TooMany,
    InvalidName
}

public enum AddAccountStatus
{
    Created,
    NameTaken,
    EmailTaken
}

public sealed record AccountCredential(string AccountName, string PasswordHash, string PasswordSalt, int Iterations);

/// <summary>
/// One saved stack. <see cref="EquippedSlot"/> is a paperdoll index, or -1 in the bag.
/// <see cref="EnchantLevel"/> is 0 for a stack and for an unenchanted piece. Older saves omit it and load as 0.
/// <see cref="ObjectId"/> is the durable Mobius-style instance id; 0 means allocate on next persist/load.
/// Null <c>Items</c> on a character means the kit was never granted.
/// </summary>
public sealed record StoredItem(int ItemId, int Count, int EquippedSlot, int EnchantLevel = 0, int ObjectId = 0);

/// <summary>
/// One Mobius <c>character_shortcuts</c> row. <see cref="Slot"/> is 0..11 on <see cref="Page"/> 0..9.
/// <see cref="Type"/> is 1 item (id = item object id), 2 skill (id = skill id, level = skill level),
/// 3 action, 4 macro, 5 recipe. Null <c>Shortcuts</c> on a character means none were ever saved.
/// </summary>
public sealed record StoredShortcut(int Slot, int Page, int Type, int Id, int Level)
{
    public const int SlotsPerPage = 12;
    public const int MaxPages = 10;
    public const int TypeItem = 1;
    public const int TypeSkill = 2;
    public const int TypeAction = 3;
    public const int TypeMacro = 4;
    public const int TypeRecipe = 5;

    /// <summary>Client world slot (slot + page * 12).</summary>
    public int WorldSlot => Slot + Page * SlotsPerPage;

    public bool IsValid =>
        Slot >= 0 && Slot < SlotsPerPage &&
        Page >= 0 && Page < MaxPages &&
        Type >= TypeItem && Type <= TypeRecipe &&
        Id > 0 && Level >= 0;
}

/// <summary>
/// One Mobius <c>character_skills</c> row: a skill learned from a trainer (or quest). Auto-granted skills
/// are not stored; they are re-derived from the class tree at enter. Null <c>LearnedSkills</c> = none saved.
/// <para>
/// Interlude skill enchant (<c>RequestExEnchantSkill</c>) does <b>not</b> use a separate column: Mobius writes the
/// enchanted skill level into the same <c>skill_level</c> field (enchanted ranks are extra levels on the skill
/// definition). When Fineage wires EnchantSkill, bump <see cref="Level"/> here and rebuild KnownSkills — the
/// passive fold already reads that level for <c>ValueAt</c>.
/// </para>
/// </summary>
public sealed record StoredSkill(int SkillId, int Level)
{
    public bool IsValid => SkillId > 0 && Level > 0 && Level < 1000;
}

/// <summary>
/// Mobius <c>character_skills_save</c> row: active abnormal remaining time across logout.
/// </summary>
public sealed record StoredEffect(int SkillId, int Level, int RemainingSeconds)
{
    public bool IsValid => SkillId > 0 && Level > 0 && RemainingSeconds > 0;
}

public sealed record CharacterRecord(
    int Id,
    string Name,
    string Account,
    int ClassId,
    int Race,
    int Sex,
    int Level,
    IReadOnlyList<StoredItem>? Items = null,
    int HairStyle = 0,
    int HairColor = 0,
    int Face = 0,
    bool HasPosition = false,
    int X = 0,
    int Y = 0,
    int Z = 0,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastAccess = default,
    DateTimeOffset? DeleteAt = null,
    int MaxHp = 0,
    int Hp = 0,
    int MaxMp = 0,
    int Mp = 0,
    int Sp = 0,
    long Adena = 0,
    string Title = "",
    int ClanId = 0,
    long ClanJoinExpiryUnixMs = 0,
    long ClanCreateExpiryUnixMs = 0,
    long Exp = 0,
    IReadOnlyList<QuestSaveRow>? Quests = null,
    IReadOnlyList<StoredItem>? Warehouse = null,
    bool Newbie = true,
    IReadOnlyList<StoredShortcut>? Shortcuts = null,
    IReadOnlyList<StoredSkill>? LearnedSkills = null,
    int DeathPenaltyLevel = 0,
    int Karma = 0,
    int PkKills = 0,
    int PvpKills = 0,
    IReadOnlyList<int>? KnownRecipes = null,
    IReadOnlyList<StoredSocial>? Friends = null,
    IReadOnlyList<StoredSocial>? Blocked = null,
    bool BlockAll = false,
    int HennaDye1 = 0,
    int HennaDye2 = 0,
    int HennaDye3 = 0,
    IReadOnlyList<StoredEffect>? Effects = null)
{
    /// <summary>Remaining whole seconds until hard delete. Zero when the character is not pending.</summary>
    public int DeleteSecondsLeft(DateTimeOffset now)
    {
        if (DeleteAt is not { } at)
            return 0;
        long remainingMs = at.ToUnixTimeMilliseconds() - now.ToUnixTimeMilliseconds();
        if (remainingMs <= 0)
            return 0;
        return (int)(remainingMs / 1000);
    }

    public bool IsPendingDelete => DeleteAt != null;
}

/// <summary>One friend or block-list entry (Mobius character_friends row with a name snapshot).</summary>
public sealed record StoredSocial(int ObjectId, string Name);

/// <summary>One persisted clan member. <see cref="CharacterId"/> is the zone object id.</summary>
public sealed record ClanMemberRecord(
    int CharacterId,
    string Account,
    string Name,
    int ClassId,
    int Level,
    int PledgeType);

/// <summary>
/// Persisted clan. Crest id stays 0 until crests exist. Times are unix milliseconds; 0 means none.
/// <see cref="CastleId"/> is Mobius <c>clan_data.hasCastle</c>. 0 means the clan owns no castle.
/// </summary>
public sealed record ClanRecord(
    int Id,
    string Name,
    int Level,
    int LeaderId,
    int CrestId,
    long DissolvingExpiryUnixMs,
    long CharPenaltyExpiryUnixMs,
    IReadOnlyList<ClanMemberRecord> Members,
    int CastleId = 0,
    /// <summary>Mobius <c>clan_skills</c> book (id → level). Applied to online members via ZoneEntity.ClanSkills.</summary>
    IReadOnlyDictionary<int, int>? Skills = null);

/// <summary>Persisted castle row. Owner 0 is an NPC castle. Tax is a percent. Times are unix milliseconds.</summary>
public sealed record CastleSave(
    int Id,
    int OwnerClanId,
    int TaxPercent,
    long SiegeUnixMs,
    bool RegistrationClosed,
    bool ScheduleAccepted,
    bool InProgress,
    long EndsUnixMs);

/// <summary>One <c>siege_clans</c> row. Owner clans are not stored; side is attacker, defender, or pending.</summary>
public sealed record SiegeClanSave(int CastleId, int ClanId, int Side);

/// <summary>Character clan id and penalty expiries written with a clan mutation.</summary>
public readonly record struct CharacterClanState(
    string Account,
    int CharacterId,
    int ClanId,
    long JoinExpiryUnixMs,
    long CreateExpiryUnixMs);
