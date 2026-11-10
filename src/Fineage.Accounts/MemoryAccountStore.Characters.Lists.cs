using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
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
