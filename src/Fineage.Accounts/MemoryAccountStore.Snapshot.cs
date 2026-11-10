using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
    void LoadFromJson(string json)
    {
        var data = JsonSerializer.Deserialize<StoreFile>(json, JsonOptions)
            ?? throw new InvalidDataException("empty account store snapshot");
        if (data.Version != 1)
            throw new InvalidDataException($"unsupported account store version {data.Version}");
        data.Clans ??= [];
        data.Castles ??= [];
        data.SiegeClans ??= [];
        if (data.NextItemObjectId < PlayerInventory.FirstObjectId)
            data.NextItemObjectId = PlayerInventory.FirstObjectId;
        EnsureItemObjectIds(data);
        _data = data;
    }

    void Save()
    {
        string snapshot = JsonSerializer.Serialize(_data, JsonOptions);
        if (EmitSnapshots)
            _afterSaveSnapshot?.Invoke(snapshot);
    }

    List<StoredItemDto>? CopyItemsAssigningIds(IReadOnlyList<StoredItem>? items, HashSet<int>? releaseObjectIds)
    {
        if (items == null)
            return null;
        var taken = AllObjectIdsUnlocked();
        if (releaseObjectIds != null)
        {
            foreach (int id in releaseObjectIds)
                taken.Remove(id);
        }

        var rows = new List<StoredItemDto>(items.Count);
        var batch = new HashSet<int>();
        foreach (var item in items)
        {
            int objectId = item.ObjectId;
            if (objectId <= 0 || taken.Contains(objectId) || batch.Contains(objectId))
                objectId = NextItemObjectIdUnlocked(taken);
            else if (objectId >= _data.NextItemObjectId)
                _data.NextItemObjectId = checked(objectId + 1);
            batch.Add(objectId);
            taken.Add(objectId);
            rows.Add(new StoredItemDto
            {
                ItemId = item.ItemId,
                Count = item.Count,
                EquippedSlot = item.EquippedSlot,
                EnchantLevel = item.EnchantLevel,
                ObjectId = objectId
            });
        }

        return rows;
    }

    static HashSet<int> ObjectIdSet(List<StoredItemDto>? items)
    {
        var set = new HashSet<int>();
        if (items == null)
            return set;
        foreach (var item in items)
        {
            if (item.ObjectId > 0)
                set.Add(item.ObjectId);
        }

        return set;
    }

    HashSet<int> AllObjectIdsUnlocked()
    {
        var taken = new HashSet<int>();
        foreach (var account in _data.Accounts)
        {
            foreach (var character in account.Characters)
            {
                foreach (int id in ObjectIdSet(character.Items))
                    taken.Add(id);
                foreach (int id in ObjectIdSet(character.Warehouse))
                    taken.Add(id);
            }
        }

        return taken;
    }

    int NextItemObjectIdUnlocked(HashSet<int>? taken = null)
    {
        if (_data.NextItemObjectId < PlayerInventory.FirstObjectId)
            _data.NextItemObjectId = PlayerInventory.FirstObjectId;
        while (taken != null && taken.Contains(_data.NextItemObjectId))
            _data.NextItemObjectId++;
        int id = _data.NextItemObjectId++;
        taken?.Add(id);
        return id;
    }

    static void EnsureItemObjectIds(StoreFile data)
    {
        int next = data.NextItemObjectId < PlayerInventory.FirstObjectId
            ? PlayerInventory.FirstObjectId
            : data.NextItemObjectId;
        var taken = new HashSet<int>();
        foreach (var account in data.Accounts)
        {
            foreach (var character in account.Characters)
            {
                next = AssignMissingIds(character.Items, next, taken);
                next = AssignMissingIds(character.Warehouse, next, taken);
            }
        }

        data.NextItemObjectId = next;
    }

    static int AssignMissingIds(List<StoredItemDto>? items, int next, HashSet<int> taken)
    {
        if (items == null)
            return next;
        foreach (var item in items)
        {
            if (item.ObjectId <= 0 || !taken.Add(item.ObjectId))
            {
                while (!taken.Add(next))
                    next++;
                item.ObjectId = next++;
            }
            else if (item.ObjectId >= next)
            {
                next = item.ObjectId + 1;
            }
        }

        return next;
    }

    static CharacterRecord ToRecord(string account, CharacterDto character) =>
        new(
            character.Id,
            character.Name,
            account,
            character.ClassId,
            character.Race,
            character.Sex,
            character.Level,
            character.Items == null
                ? null
                : character.Items.Select(item => new StoredItem(item.ItemId, item.Count, item.EquippedSlot, item.EnchantLevel, item.ObjectId)).ToArray(),
            character.HairStyle,
            character.HairColor,
            character.Face,
            character.HasPosition,
            character.X,
            character.Y,
            character.Z,
            character.CreatedAt,
            character.LastAccess,
            character.DeleteAt,
            character.MaxHp,
            character.Hp,
            character.MaxMp,
            character.Mp,
            character.Sp,
            character.Adena,
            character.Title,
            character.ClanId,
            character.ClanJoinExpiryUnixMs,
            character.ClanCreateExpiryUnixMs,
            character.Exp,
            character.Quests == null
                ? null
                : character.Quests.Select(quest => new QuestSaveRow(
                    quest.QuestId,
                    quest.State,
                    (quest.Vars ?? []).Select(variable => new QuestSaveVar(variable.Key, variable.Value)).ToArray())).ToArray(),
            character.Warehouse == null
                ? null
                : character.Warehouse.Select(item => new StoredItem(item.ItemId, item.Count, -1, item.EnchantLevel, item.ObjectId)).ToArray(),
            character.Newbie,
            character.Shortcuts == null
                ? null
                : character.Shortcuts.Select(s => new StoredShortcut(s.Slot, s.Page, s.Type, s.Id, s.Level)).ToArray(),
            character.LearnedSkills == null
                ? null
                : character.LearnedSkills.Select(s => new StoredSkill(s.SkillId, s.Level)).ToArray(),
            character.DeathPenaltyLevel,
            character.Karma,
            character.PkKills,
            character.PvpKills,
            character.KnownRecipes?.ToArray(),
            character.Friends == null
                ? null
                : character.Friends.Select(f => new StoredSocial(f.ObjectId, f.Name ?? "")).ToArray(),
            character.Blocked == null
                ? null
                : character.Blocked.Select(f => new StoredSocial(f.ObjectId, f.Name ?? "")).ToArray(),
            character.BlockAll,
            character.HennaDye1,
            character.HennaDye2,
            character.HennaDye3,
            character.Effects == null
                ? null
                : character.Effects.Select(e => new StoredEffect(e.SkillId, e.Level, e.RemainingSeconds)).ToArray());

    sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public int NextCharacterId { get; set; } = 1;
        public int NextClanId { get; set; } = 1;
        public int NextItemObjectId { get; set; } = PlayerInventory.FirstObjectId;
        public List<AccountDto> Accounts { get; set; } = [];
        public List<ClanDto> Clans { get; set; } = [];
        public List<CastleDto> Castles { get; set; } = [];
        public List<SiegeClanDto> SiegeClans { get; set; } = [];
    }

    sealed class AccountDto
    {
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public int Iterations { get; set; }
        public List<CharacterDto> Characters { get; set; } = [];
    }

    sealed class CharacterDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int Level { get; set; } = 1;
        public int HairStyle { get; set; }
        public int HairColor { get; set; }
        public int Face { get; set; }
        public bool HasPosition { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAccess { get; set; }
        public DateTimeOffset? DeleteAt { get; set; }
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int MaxMp { get; set; }
        public int Mp { get; set; }
        public int Sp { get; set; }
        public long Adena { get; set; }
        public string Title { get; set; } = "";
        public int ClanId { get; set; }
        public long ClanJoinExpiryUnixMs { get; set; }
        public long ClanCreateExpiryUnixMs { get; set; }
        public long Exp { get; set; }
        public List<StoredQuestDto>? Quests { get; set; }
        public List<StoredItemDto>? Items { get; set; }
        public List<StoredItemDto>? Warehouse { get; set; }
        public bool Newbie { get; set; } = true;
        public List<StoredShortcutDto>? Shortcuts { get; set; }
        public List<StoredSkillDto>? LearnedSkills { get; set; }
        public int DeathPenaltyLevel { get; set; }
        public int Karma { get; set; }
        public int PkKills { get; set; }
        public int PvpKills { get; set; }
        public List<int>? KnownRecipes { get; set; }
        public List<StoredSocialDto>? Friends { get; set; }
        public List<StoredSocialDto>? Blocked { get; set; }
        public bool BlockAll { get; set; }
        public int HennaDye1 { get; set; }
        public int HennaDye2 { get; set; }
        public int HennaDye3 { get; set; }
        public List<StoredEffectDto>? Effects { get; set; }
    }

    sealed class StoredSocialDto
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
    }

    sealed class StoredSkillDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
    }

    sealed class StoredEffectDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int RemainingSeconds { get; set; }
    }

    sealed class StoredShortcutDto
    {
        public int Slot { get; set; }
        public int Page { get; set; }
        public int Type { get; set; }
        public int Id { get; set; }
        public int Level { get; set; }
    }

    sealed class StoredQuestDto
    {
        public int QuestId { get; set; }
        public int State { get; set; }
        public List<StoredQuestVarDto>? Vars { get; set; }
    }

    sealed class StoredQuestVarDto
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    sealed class ClanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int LeaderId { get; set; }
        public int CrestId { get; set; }
        public long DissolvingExpiryUnixMs { get; set; }
        public long CharPenaltyExpiryUnixMs { get; set; }
        public int CastleId { get; set; }
        public Dictionary<int, int>? Skills { get; set; }
        public List<ClanMemberDto> Members { get; set; } = [];
    }

    sealed class CastleDto
    {
        public int Id { get; set; }
        public int OwnerClanId { get; set; }
        public int TaxPercent { get; set; }
        public long SiegeUnixMs { get; set; }
        public bool RegistrationClosed { get; set; }
        public bool ScheduleAccepted { get; set; }
        public bool InProgress { get; set; }
        public long EndsUnixMs { get; set; }
    }

    sealed class SiegeClanDto
    {
        public int CastleId { get; set; }
        public int ClanId { get; set; }
        public int Side { get; set; }
    }

    sealed class ClanMemberDto
    {
        public int CharacterId { get; set; }
        public string Account { get; set; } = "";
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int PledgeType { get; set; }
    }

    sealed class StoredItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EquippedSlot { get; set; } = -1;
        public int EnchantLevel { get; set; }
        public int ObjectId { get; set; }
    }
}
