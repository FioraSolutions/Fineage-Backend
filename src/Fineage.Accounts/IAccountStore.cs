using Fineage.Game.Quests;

namespace Fineage.Accounts;

public interface IAccountStore
{
    bool AccountExists(string account);

    AddAccountStatus TryAddAccount(string name, string passwordHash, string passwordSalt, int iterations, string? email);

    bool TryGetCredentialByEmail(string email, out AccountCredential credential);

    bool TryGetCredential(string account, out AccountCredential credential);

    CreateCharacterStatus TryAddCharacter(
        string account,
        string name,
        int classId,
        int race,
        int sex,
        out CharacterRecord? created,
        IReadOnlyList<StoredItem>? items = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? now = null,
        int hairStyle = 0,
        int hairColor = 0,
        int face = 0,
        bool hasPosition = false,
        int x = 0,
        int y = 0,
        int z = 0,
        int level = 1,
        int maxHp = 0,
        int hp = 0,
        int maxMp = 0,
        int mp = 0,
        int sp = 0,
        long adena = 0,
        string title = "",
        int? maxCharacters = null);

    /// <summary>Replaces the saved bag. An empty list is a real save; it does not mean "grant the kit".</summary>
    bool TryReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items);

    /// <summary>Replaces the private warehouse. An empty list is an empty warehouse.</summary>
    bool TryReplaceWarehouse(string account, int characterId, IReadOnlyList<StoredItem> items);

    /// <summary>Writes the last world position. When <paramref name="lastAccess"/> is set and later than the stored value, updates last played.</summary>
    bool TrySavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess);

    /// <summary>Writes current HP/MP after zone play. Maxima are unchanged.</summary>
    bool TrySaveVitals(string account, int characterId, int hp, int mp);

    /// <summary>Writes level, experience, SP, and the vitals that a level change may have raised.</summary>
    bool TrySaveProgress(string account, int characterId, int level, long exp, int sp, int maxHp, int hp, int maxMp, int mp);

    /// <summary>Writes the class after a class transfer (Mobius setClassId / setBaseClass).</summary>
    bool TrySaveClass(string account, int characterId, int classId);

    /// <summary>
    /// Writes the Death Penalty level (Mobius <c>characters.death_penalty_level</c>, <c>Player.java:337</c>). It
    /// outlives the session, so the zone writes it the moment a death rolls it up.
    /// </summary>
    bool TrySaveDeathPenalty(string account, int characterId, int level);

    /// <summary>
    /// Writes the three henna dye slot ids (Mobius <c>character_hennas</c> / player henna array).
    /// </summary>
    bool TrySaveHenna(string account, int characterId, int dye1, int dye2, int dye3);

    /// <summary>
    /// Writes karma / PK / PvP kill counters (Mobius <c>characters.karma|pkkills|pvpkills</c>).
    /// </summary>
    bool TrySaveKarmaPk(string account, int characterId, int karma, int pkKills, int pvpKills);

    /// <summary>Replace the character's known craft recipe list ids (Mobius recipe book).</summary>
    bool TryReplaceKnownRecipes(string account, int characterId, IReadOnlyList<int> recipeIds);

    /// <summary>Replace the character's friend list (Mobius character_friends relation=0).</summary>
    bool TryReplaceFriends(string account, int characterId, IReadOnlyList<StoredSocial> friends);

    /// <summary>Replace the character's block list + block-all flag (Mobius relation=1 / blockAll).</summary>
    bool TryReplaceBlocked(string account, int characterId, IReadOnlyList<StoredSocial> blocked, bool blockAll);

    /// <summary>Global character lookup by id (names are unique across accounts).</summary>
    CharacterRecord? FindByCharacterId(int characterId);

    /// <summary>Global character lookup by name (case-insensitive).</summary>
    CharacterRecord? FindByCharacterName(string name);

    /// <summary>Replaces the character's quest rows. An empty list is a real save.</summary>
    bool TryReplaceQuests(string account, int characterId, IReadOnlyList<QuestSaveRow> quests);

    /// <summary>Replaces the character's hotbar (Mobius character_shortcuts). An empty list is a real save.</summary>
    bool TryReplaceShortcuts(string account, int characterId, IReadOnlyList<StoredShortcut> shortcuts);

    /// <summary>Replaces the character's learned skills (Mobius character_skills). An empty list is a real save.</summary>
    bool TryReplaceLearnedSkills(string account, int characterId, IReadOnlyList<StoredSkill> skills);

    /// <summary>Replaces active effects (Mobius character_skills_save). An empty list clears stored buffs.</summary>
    bool TryReplaceEffects(string account, int characterId, IReadOnlyList<StoredEffect> effects);

    bool TrySetLastAccess(string account, int characterId, DateTimeOffset when);

    bool TrySetDeleteAt(string account, int characterId, DateTimeOffset deleteAt);

    bool TryClearDeletion(string account, int characterId);

    /// <summary>Removes the character and the items stored on it. The name can be used again.</summary>
    bool TryHardDelete(string account, int characterId);

    CharacterRecord? FindById(string account, int characterId);

    CharacterRecord? FindByName(string account, string name);

    /// <summary>Creation order, after hard-deleting characters whose delete time is already in the past.</summary>
    IReadOnlyList<CharacterRecord> List(string account, DateTimeOffset now);

    IReadOnlyList<CharacterRecord> List(string account);

    IReadOnlyList<ClanRecord> ListClans();

    ClanRecord? FindClan(int clanId);

    ClanRecord? FindClanByName(string name);

    /// <summary>Assigns a new id. Sets <see cref="CharacterRecord.ClanId"/> on every member. Leader penalties come from <paramref name="leader"/>.</summary>
    ClanRecord? InsertClan(ClanRecord draft, CharacterClanState leader);

    /// <summary>Replaces the clan row and writes <paramref name="characters"/>.</summary>
    bool UpdateClan(ClanRecord clan, IReadOnlyList<CharacterClanState> characters);

    bool DeleteClan(int clanId, IReadOnlyList<CharacterClanState> characters);

    bool TrySetCharacterClanState(CharacterClanState state);

    IReadOnlyList<CastleSave> ListCastles();

    IReadOnlyList<SiegeClanSave> ListSiegeClans();

    /// <summary>Replaces castle rows and siege registrations, and writes each clan's <see cref="ClanRecord.CastleId"/>.</summary>
    bool SaveSiege(IReadOnlyList<CastleSave> castles, IReadOnlyList<SiegeClanSave> clans, IReadOnlyDictionary<int, int> clanCastleIds);
}
