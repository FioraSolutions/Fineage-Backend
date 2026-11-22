using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Zone.World;

/// <summary>
/// Write facade over <see cref="IAccountStore"/>. Currency, items, warehouse and freight stay on the caller and
/// commit with the economy ledger before the client is told success. Every other character write is an immutable
/// snapshot handed to <see cref="CharacterPersistWorker"/> once that worker is running (the hosted simulation).
/// Hand-driven worlds (tests, hooks before <c>StartSimulation</c>) still write inline, because there is no worker.
/// </summary>
sealed class ZoneCharacterPersist
{
    const string Character = CharacterPersistWorker.CharacterDomain;
    const string Pet = CharacterPersistWorker.PetDomain;

    readonly IAccountStore? _accounts;
    readonly CharacterPersistWorker _worker;

    public ZoneCharacterPersist(IAccountStore? accounts, CharacterPersistWorker worker)
    {
        _accounts = accounts;
        _worker = worker;
    }

    public bool Enabled => _accounts != null;

    /// <summary>
    /// Economic commit (caller thread) or a versioned snapshot. <paramref name="apply"/> for a snapshot must close
    /// only over copies, never over live entity fields.
    /// </summary>
    bool Write(string kind, string domain, int ownerId, string account, bool economic, Func<IAccountStore, bool> apply)
    {
        if (_accounts == null)
            return true;
        if (economic || !_worker.IsRunning)
            return apply(_accounts);
        _worker.Enqueue(domain, ownerId, kind, account, apply);
        return true;
    }

    static T[] Copy<T>(IReadOnlyList<T> rows)
    {
        var copy = new T[rows.Count];
        for (int i = 0; i < rows.Count; i++)
            copy[i] = rows[i];
        return copy;
    }

    static QuestSaveRow[] CopyQuests(IReadOnlyList<QuestSaveRow> quests)
    {
        var copy = new QuestSaveRow[quests.Count];
        for (int i = 0; i < quests.Count; i++)
        {
            var row = quests[i];
            copy[i] = row with { Vars = Copy(row.Vars) };
        }

        return copy;
    }

    public void SavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess) =>
        Write(nameof(SavePresence), Character, characterId, account, economic: false,
            store => store.TrySavePresence(account, characterId, x, y, z, lastAccess));

    public void SaveVitals(string account, int characterId, int hp, int mp, int cp) =>
        Write(nameof(SaveVitals), Character, characterId, account, economic: false,
            store => store.TrySaveVitals(account, characterId, hp, mp, cp));

    public void SaveProgress(
        string account,
        int characterId,
        int level,
        long exp,
        int sp,
        int maxHp,
        int hp,
        int maxMp,
        int mp,
        int maxCp,
        int cp) =>
        Write(nameof(SaveProgress), Character, characterId, account, economic: false,
            store => store.TrySaveProgress(account, characterId, level, exp, sp, maxHp, hp, maxMp, mp, maxCp, cp));

    public StoredPet? FindPet(int controlObjectId) => _accounts?.FindPet(controlObjectId);

    public void SavePet(StoredPet pet)
    {
        var items = Copy(pet.Items);
        var snapshot = pet with { Items = items };
        Write(nameof(SavePet), Pet, snapshot.ControlObjectId, "", economic: false, store => store.SavePet(snapshot));
    }

    public void DeletePet(int controlObjectId) =>
        Write(nameof(DeletePet), Pet, controlObjectId, "", economic: false, store => store.DeletePet(controlObjectId));

    public void SaveClass(string account, int characterId, int classId) =>
        Write(nameof(SaveClass), Character, characterId, account, economic: false,
            store => store.TrySaveClass(account, characterId, classId));

    public void SaveClassState(string account, int characterId, CharacterClassState state)
    {
        var snapshot = state.Clone();
        Write(nameof(SaveClassState), Character, characterId, account, economic: false,
            store => store.TrySaveClassState(account, characterId, snapshot));
    }

    public void SaveDeathPenalty(string account, int characterId, int level) =>
        Write(nameof(SaveDeathPenalty), Character, characterId, account, economic: false,
            store => store.TrySaveDeathPenalty(account, characterId, level));

    public void SaveHenna(string account, int characterId, int dye1, int dye2, int dye3) =>
        Write(nameof(SaveHenna), Character, characterId, account, economic: false,
            store => store.TrySaveHenna(account, characterId, dye1, dye2, dye3));

    public void SaveKarmaPk(string account, int characterId, int karma, int pkKills, int pvpKills) =>
        Write(nameof(SaveKarmaPk), Character, characterId, account, economic: false,
            store => store.TrySaveKarmaPk(account, characterId, karma, pkKills, pvpKills));

    public bool ReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        Write(nameof(ReplaceItems), Character, characterId, account, economic: true,
            store => store.TryReplaceItems(account, characterId, items));

    public bool ReplaceWarehouse(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        Write(nameof(ReplaceWarehouse), Character, characterId, account, economic: true,
            store => store.TryReplaceWarehouse(account, characterId, items));

    public bool ReplaceFreight(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        Write(nameof(ReplaceFreight), Character, characterId, account, economic: true,
            store => store.TryReplaceFreight(account, characterId, items));

    public void ReplaceQuests(string account, int characterId, IReadOnlyList<QuestSaveRow> quests)
    {
        var copy = CopyQuests(quests);
        Write(nameof(ReplaceQuests), Character, characterId, account, economic: false,
            store => store.TryReplaceQuests(account, characterId, copy));
    }

    public void ReplaceLearnedSkills(string account, int characterId, IReadOnlyList<StoredSkill> skills)
    {
        var copy = Copy(skills);
        Write(nameof(ReplaceLearnedSkills), Character, characterId, account, economic: false,
            store => store.TryReplaceLearnedSkills(account, characterId, copy));
    }

    public void ReplaceKnownRecipes(string account, int characterId, IReadOnlyList<int> recipeIds)
    {
        var copy = Copy(recipeIds);
        Write(nameof(ReplaceKnownRecipes), Character, characterId, account, economic: false,
            store => store.TryReplaceKnownRecipes(account, characterId, copy));
    }

    public void ReplaceFriends(string account, int characterId, IReadOnlyList<StoredSocial> friends)
    {
        var copy = Copy(friends);
        Write(nameof(ReplaceFriends), Character, characterId, account, economic: false,
            store => store.TryReplaceFriends(account, characterId, copy));
    }

    public void ReplaceBlocked(string account, int characterId, IReadOnlyList<StoredSocial> blocked, bool blockAll)
    {
        var copy = Copy(blocked);
        Write(nameof(ReplaceBlocked), Character, characterId, account, economic: false,
            store => store.TryReplaceBlocked(account, characterId, copy, blockAll));
    }

    public bool ReplaceShortcuts(string account, int characterId, IReadOnlyList<StoredShortcut> shortcuts)
    {
        var copy = Copy(shortcuts);
        return Write(nameof(ReplaceShortcuts), Character, characterId, account, economic: false,
            store => store.TryReplaceShortcuts(account, characterId, copy));
    }

    public void ReplaceEffects(string account, int characterId, IReadOnlyList<StoredEffect> effects)
    {
        var copy = Copy(effects);
        Write(nameof(ReplaceEffects), Character, characterId, account, economic: false,
            store => store.TryReplaceEffects(account, characterId, copy));
    }

    public bool ReplaceItemsFromInventory(string account, int characterId, PlayerInventory inventory) =>
        ReplaceItems(account, characterId, CharacterInventory.Rows(inventory));

    public void SaveQuestProgress(string account, int characterId, ZoneEntity entity)
    {
        if (_accounts == null)
            return;
        ReplaceQuests(account, characterId, entity.Quests.Save());
        SaveProgress(
            account,
            characterId,
            entity.Level,
            entity.Exp,
            entity.Sp,
            entity.MaxHp,
            entity.CurHp,
            entity.MaxMp,
            entity.CurMp,
            entity.MaxCp,
            entity.CurCp);
    }
}
