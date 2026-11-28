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
        data.Crests ??= [];
        data.ClanHalls ??= [];
        data.Pets ??= [];
        data.WorldState ??= [];
        data.WorldStateImports ??= [];
        if (data.NextItemObjectId < PlayerInventory.FirstObjectId)
            data.NextItemObjectId = PlayerInventory.FirstObjectId;
        EnsureItemObjectIds(data);
        _data = data;
    }

    /// <summary>
    /// Serializes the whole store only when someone will receive the snapshot. It used to serialize first and then
    /// drop the JSON on silent paths: every Postgres hot-path write (presence, vitals, progress, quests) built a
    /// multi-megabyte string of all accounts under <c>_gate</c>, which held the lock the simulation's store reads
    /// wait on and churned ~100 MB/s of large-object strings at 200 players.
    /// </summary>
    void Save()
    {
        if (!EmitSnapshots || _afterSaveSnapshot is null)
            return;
        string json = JsonSerializer.Serialize(_data, JsonOptions);
        lock (_snapshotGate)
        {
            _pendingSnapshot = json;
            _pendingSnapshotSequence++;
        }

        MarkSnapshotDirty(this);
    }

    /// <summary>
    /// Runs the snapshot callback with no store lock held. Snapshots are serialized (a newer sequence always wins)
    /// so a slow durable write cannot publish an older image after a newer one.
    /// </summary>
    void FlushPendingSnapshot()
    {
        lock (_snapshotWriteGate)
        {
            while (true)
            {
                string? json;
                long sequence;
                lock (_snapshotGate)
                {
                    if (_pendingSnapshot is null)
                        return;
                    json = _pendingSnapshot;
                    sequence = _pendingSnapshotSequence;
                    _pendingSnapshot = null;
                    if (sequence <= _lastWrittenSnapshotSequence)
                        continue;
                    _lastWrittenSnapshotSequence = sequence;
                }

                System.Diagnostics.Debug.Assert(
                    !Monitor.IsEntered(_gate),
                    "AfterSaveSnapshot must run after the memory store lock is released");
                if (_afterSaveSnapshot is { } handler)
                    handler(json);
            }
        }
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
                ObjectId = objectId,
                CustomType1 = item.CustomType1,
                CustomType2 = item.CustomType2,
                Option1 = item.Option1,
                Option2 = item.Option2
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
                foreach (int id in ObjectIdSet(character.Freight))
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
                next = AssignMissingIds(character.Freight, next, taken);
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
                : character.Items.Select(item => new StoredItem(
                    item.ItemId, item.Count, item.EquippedSlot, item.EnchantLevel, item.ObjectId,
                    item.CustomType1, item.CustomType2, item.Option1, item.Option2)).ToArray(),
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
                : character.Warehouse.Select(item => new StoredItem(
                    item.ItemId, item.Count, -1, item.EnchantLevel, item.ObjectId,
                    item.CustomType1, item.CustomType2, item.Option1, item.Option2)).ToArray(),
            character.Freight == null
                ? null
                : character.Freight.Select(item => new StoredItem(
                    item.ItemId, item.Count, -1, item.EnchantLevel, item.ObjectId,
                    item.CustomType1, item.CustomType2, item.Option1, item.Option2)).ToArray(),
            character.Newbie,
            character.Shortcuts == null
                ? null
                : character.Shortcuts.Select(s => new StoredShortcut(s.Slot, s.Page, s.Type, s.Id, s.Level)).ToArray(),
            character.Macros == null
                ? null
                : character.Macros.Select(m => new StoredMacro(
                    m.Id, m.Icon, m.Name ?? "", m.Descr ?? "", m.Acronym ?? "",
                    (m.Commands ?? []).Select(c => new StoredMacroCmd(c.Entry, c.Type, c.D1, c.D2, c.Cmd ?? "")).ToArray())).ToArray(),
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
                : character.Effects.Select(e => new StoredEffect(
                    e.SkillId, e.Level, e.RemainingSeconds, e.ReuseDelayMs, e.SystimeMs, e.RestoreType)).ToArray(),
            character.ClassState?.Clone(),
            character.MaxCp,
            character.Cp);
}
