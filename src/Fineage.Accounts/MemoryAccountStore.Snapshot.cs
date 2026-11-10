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
}
