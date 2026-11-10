using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
    public CreateCharacterStatus TryAddCharacter(
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
        int? maxCharacters = null)
    {
        created = null;
        if (!NameRules.IsCharacterName(name))
            return CreateCharacterStatus.InvalidName;
        name = name.Trim();
        if (items != null)
            CharacterInventory.Validate(items);

        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
                return CreateCharacterStatus.AccountMissing;
            if (now is { } purgeAt && PurgeUnlocked(row, purgeAt) > 0)
                Save();
            int cap = maxCharacters ?? HumanClasses.MaxCharactersPerAccount;
            if (cap > 0 && row.Characters.Count >= cap)
                return CreateCharacterStatus.TooMany;
            if (NameTaken(name))
                return CreateCharacterStatus.NameTaken;

            if (_data.NextCharacterId >= NpcObjectIdAllocator.LegacyFirst)
            {
                throw new InvalidOperationException(
                    $"Character id {_data.NextCharacterId} would enter the NPC object id range at {NpcObjectIdAllocator.LegacyFirst}.");
            }

            var character = new CharacterDto
            {
                Id = _data.NextCharacterId++,
                Name = name,
                ClassId = classId,
                Race = race,
                Sex = sex,
                Level = level,
                HairStyle = hairStyle,
                HairColor = hairColor,
                Face = face,
                HasPosition = hasPosition,
                X = x,
                Y = y,
                Z = z,
                MaxHp = maxHp,
                Hp = hp,
                MaxMp = maxMp,
                Mp = mp,
                Sp = sp,
                Adena = adena,
                Title = title ?? "",
                Items = CopyItemsAssigningIds(items, releaseObjectIds: null),
                Newbie = row.Characters.Count == 0,
                CreatedAt = createdAt ?? DateTimeOffset.UtcNow
            };
            row.Characters.Add(character);
            Save();
            created = ToRecord(row.Name, character);
            return CreateCharacterStatus.Ok;
        }
    }

    public bool TryReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CharacterInventory.Validate(items);
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            var release = ObjectIdSet(character.Items);
            character.Items = CopyItemsAssigningIds(items, release);
            Save();
            return true;
        }
    }

    public bool TryReplaceWarehouse(string account, int characterId, IReadOnlyList<StoredItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CharacterInventory.Validate(items);
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            var release = ObjectIdSet(character.Warehouse);
            character.Warehouse = CopyItemsAssigningIds(items, release);
            Save();
            return true;
        }
    }

    public bool TrySavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.HasPosition = true;
            character.X = x;
            character.Y = y;
            character.Z = z;
            if (lastAccess is { } access && access > character.LastAccess)
                character.LastAccess = access;
            Save();
            return true;
        }
    }

    public bool TrySaveVitals(string account, int characterId, int hp, int mp)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Hp = hp;
            character.Mp = mp;
            Save();
            return true;
        }
    }

    public bool TrySaveProgress(string account, int characterId, int level, long exp, int sp, int maxHp, int hp, int maxMp, int mp)
    {
        if (level < 1 || exp < 0 || sp < 0 || maxHp < 0 || hp < 0 || maxMp < 0 || mp < 0)
            return false;
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Level = level;
            character.Exp = exp;
            character.Sp = sp;
            character.MaxHp = maxHp;
            character.Hp = hp;
            character.MaxMp = maxMp;
            character.Mp = mp;
            Save();
            return true;
        }
    }

    public bool TrySaveClass(string account, int characterId, int classId)
    {
        if (classId < 0)
            return false;
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.ClassId = classId;
            Save();
            return true;
        }
    }

    public bool TrySaveHenna(string account, int characterId, int dye1, int dye2, int dye3)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.HennaDye1 = Math.Max(0, dye1);
            character.HennaDye2 = Math.Max(0, dye2);
            character.HennaDye3 = Math.Max(0, dye3);
            Save();
            return true;
        }
    }

    public bool TrySaveDeathPenalty(string account, int characterId, int level)
    {
        if (level < 0)
            return false;
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.DeathPenaltyLevel = level;
            Save();
            return true;
        }
    }

    public bool TrySaveKarmaPk(string account, int characterId, int karma, int pkKills, int pvpKills)
    {
        if (karma < 0 || pkKills < 0 || pvpKills < 0)
            return false;
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Karma = karma;
            character.PkKills = pkKills;
            character.PvpKills = pvpKills;
            Save();
            return true;
        }
    }

    public bool TryReplaceQuests(string account, int characterId, IReadOnlyList<QuestSaveRow> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);
        if (!ValidQuests(quests))
            return false;
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Quests = quests.Select(CopyQuest).ToList();
            Save();
            return true;
        }
    }

    public bool TryReplaceLearnedSkills(string account, int characterId, IReadOnlyList<StoredSkill> skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        var ids = new HashSet<int>();
        foreach (var s in skills)
        {
            if (s == null || !s.IsValid || !ids.Add(s.SkillId))
                return false;
        }

        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.LearnedSkills = skills.OrderBy(s => s.SkillId)
                .Select(s => new StoredSkillDto { SkillId = s.SkillId, Level = s.Level }).ToList();
            Save();
            return true;
        }
    }

    public bool TryReplaceEffects(string account, int characterId, IReadOnlyList<StoredEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var ids = new HashSet<int>();
        foreach (var e in effects)
        {
            if (e == null || !e.IsValid || !ids.Add(e.SkillId))
                return false;
        }

        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Effects = effects.OrderBy(e => e.SkillId)
                .Select(e => new StoredEffectDto
                {
                    SkillId = e.SkillId,
                    Level = e.Level,
                    RemainingSeconds = e.RemainingSeconds
                }).ToList();
            Save();
            return true;
        }
    }

    public bool TryReplaceKnownRecipes(string account, int characterId, IReadOnlyList<int> recipeIds)
    {
        ArgumentNullException.ThrowIfNull(recipeIds);
        var seen = new HashSet<int>();
        foreach (int id in recipeIds)
        {
            if (id < 1 || !seen.Add(id))
                return false;
        }

        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.KnownRecipes = recipeIds.OrderBy(id => id).ToList();
            Save();
            return true;
        }
    }

    public bool TryReplaceFriends(string account, int characterId, IReadOnlyList<StoredSocial> friends)
    {
        ArgumentNullException.ThrowIfNull(friends);
        if (!ValidateSocialList(friends))
            return false;

        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Friends = friends
                .OrderBy(f => f.ObjectId)
                .Select(f => new StoredSocialDto { ObjectId = f.ObjectId, Name = f.Name.Trim() })
                .ToList();
            Save();
            return true;
        }
    }

    public bool TryReplaceBlocked(string account, int characterId, IReadOnlyList<StoredSocial> blocked, bool blockAll)
    {
        ArgumentNullException.ThrowIfNull(blocked);
        if (!ValidateSocialList(blocked))
            return false;

        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Blocked = blocked
                .OrderBy(f => f.ObjectId)
                .Select(f => new StoredSocialDto { ObjectId = f.ObjectId, Name = f.Name.Trim() })
                .ToList();
            character.BlockAll = blockAll;
            Save();
            return true;
        }
    }

    public CharacterRecord? FindByCharacterId(int characterId)
    {
        if (characterId < 1)
            return null;
        lock (_gate)
        {
            foreach (var account in _data.Accounts)
            {
                var character = account.Characters.FirstOrDefault(c => c.Id == characterId);
                if (character != null)
                    return ToRecord(account.Name, character);
            }
            return null;
        }
    }

    public CharacterRecord? FindByCharacterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string key = name.Trim();
        lock (_gate)
        {
            foreach (var account in _data.Accounts)
            {
                var character = account.Characters.FirstOrDefault(c =>
                    string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase));
                if (character != null)
                    return ToRecord(account.Name, character);
            }
            return null;
        }
    }

    static bool ValidateSocialList(IReadOnlyList<StoredSocial> rows)
    {
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row == null || row.ObjectId < 1 || string.IsNullOrWhiteSpace(row.Name) || !seen.Add(row.ObjectId))
                return false;
        }
        return true;
    }

    public bool TryReplaceShortcuts(string account, int characterId, IReadOnlyList<StoredShortcut> shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        var slots = new HashSet<int>();
        foreach (var s in shortcuts)
        {
            if (s == null || !s.IsValid || !slots.Add(s.WorldSlot))
                return false;
        }

        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Shortcuts = shortcuts.OrderBy(s => s.WorldSlot).Select(CopyShortcut).ToList();
            Save();
            return true;
        }
    }

    static bool ValidQuests(IReadOnlyList<QuestSaveRow> quests)
    {
        var ids = new HashSet<int>();
        foreach (var quest in quests)
        {
            if (quest.QuestId <= 0 || !ids.Add(quest.QuestId))
                return false;
            if (quest.State is not ((int)QuestPhase.Created or (int)QuestPhase.Started or (int)QuestPhase.Completed))
                return false;
            if (quest.Vars == null)
                return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variable in quest.Vars)
            {
                if (!QuestJournal.IsStoredVar(variable.Key, variable.Value) || !keys.Add(variable.Key))
                    return false;
            }
        }

        return true;
    }

    static StoredQuestDto CopyQuest(QuestSaveRow quest) => new()
    {
        QuestId = quest.QuestId,
        State = quest.State,
        Vars = quest.Vars.Select(variable => new StoredQuestVarDto { Key = variable.Key, Value = variable.Value }).ToList()
    };

    public bool TrySetLastAccess(string account, int characterId, DateTimeOffset when)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.LastAccess = when;
            Save();
            return true;
        }
    }

    public bool TrySetDeleteAt(string account, int characterId, DateTimeOffset deleteAt)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.DeleteAt = deleteAt;
            Save();
            return true;
        }
    }

    public bool TryClearDeletion(string account, int characterId)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.DeleteAt = null;
            Save();
            return true;
        }
    }
}
