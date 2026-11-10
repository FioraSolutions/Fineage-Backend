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
}
