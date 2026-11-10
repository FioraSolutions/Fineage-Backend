using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Zone.World;

/// <summary>
/// Null-safe write facade over <see cref="IAccountStore"/> for ZoneWorld feature partials.
/// Reads stay on <c>_accounts</c>; all character-domain saves funnel here so call sites do not
/// repeat null checks or invent parallel persist helpers.
/// </summary>
sealed class ZoneCharacterPersist(IAccountStore? accounts)
{
    readonly IAccountStore? _accounts = accounts;

    public bool Enabled => _accounts != null;

    public void SavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess) =>
        _accounts?.TrySavePresence(account, characterId, x, y, z, lastAccess);

    public void SaveVitals(string account, int characterId, int hp, int mp) =>
        _accounts?.TrySaveVitals(account, characterId, hp, mp);

    public void SaveProgress(
        string account,
        int characterId,
        int level,
        long exp,
        int sp,
        int maxHp,
        int hp,
        int maxMp,
        int mp) =>
        _accounts?.TrySaveProgress(account, characterId, level, exp, sp, maxHp, hp, maxMp, mp);

    public void SaveClass(string account, int characterId, int classId) =>
        _accounts?.TrySaveClass(account, characterId, classId);

    public void SaveDeathPenalty(string account, int characterId, int level) =>
        _accounts?.TrySaveDeathPenalty(account, characterId, level);

    public void SaveHenna(string account, int characterId, int dye1, int dye2, int dye3) =>
        _accounts?.TrySaveHenna(account, characterId, dye1, dye2, dye3);

    public void SaveKarmaPk(string account, int characterId, int karma, int pkKills, int pvpKills) =>
        _accounts?.TrySaveKarmaPk(account, characterId, karma, pkKills, pvpKills);

    public void ReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        _accounts?.TryReplaceItems(account, characterId, items);

    public void ReplaceWarehouse(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        _accounts?.TryReplaceWarehouse(account, characterId, items);

    public void ReplaceQuests(string account, int characterId, IReadOnlyList<QuestSaveRow> quests) =>
        _accounts?.TryReplaceQuests(account, characterId, quests);

    public void ReplaceLearnedSkills(string account, int characterId, IReadOnlyList<StoredSkill> skills) =>
        _accounts?.TryReplaceLearnedSkills(account, characterId, skills);

    public void ReplaceKnownRecipes(string account, int characterId, IReadOnlyList<int> recipeIds) =>
        _accounts?.TryReplaceKnownRecipes(account, characterId, recipeIds);

    public void ReplaceFriends(string account, int characterId, IReadOnlyList<StoredSocial> friends) =>
        _accounts?.TryReplaceFriends(account, characterId, friends);

    public void ReplaceBlocked(string account, int characterId, IReadOnlyList<StoredSocial> blocked, bool blockAll) =>
        _accounts?.TryReplaceBlocked(account, characterId, blocked, blockAll);

    public void ReplaceShortcuts(string account, int characterId, IReadOnlyList<StoredShortcut> shortcuts) =>
        _accounts?.TryReplaceShortcuts(account, characterId, shortcuts);

    public void ReplaceEffects(string account, int characterId, IReadOnlyList<StoredEffect> effects) =>
        _accounts?.TryReplaceEffects(account, characterId, effects);

    public void ReplaceItemsFromInventory(string account, int characterId, PlayerInventory inventory) =>
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
            entity.CurMp);
    }
}
