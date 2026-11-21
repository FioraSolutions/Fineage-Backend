using Fineage.Game;
using Fineage.Zone.Parity;

namespace Fineage.Zone.CursedWeapons;

/// <summary>
/// In-world cursed weapon lifecycle (spawn, owner, drop on death, disappearance).
/// Disabled when <see cref="ClientHeavyFeatures.CursedWeaponsEnabled"/> is false.
/// Net is expected to push <c>ExCursedWeaponList</c> / <c>ExCursedWeaponLocation</c> when enabled.
/// </summary>
public sealed class CursedWeaponsState
{
    readonly Dictionary<int, ActiveBlade> _byItemId = new();

    public bool Enabled => ClientHeavyFeatures.CursedWeaponsEnabled;

    public bool IsActivated(int itemId) =>
        Enabled && _byItemId.TryGetValue(itemId, out var blade) && blade.Activated;

    public IReadOnlyList<int> ActivatedItemIds
    {
        get
        {
            if (!Enabled)
                return [];
            var list = new List<int>(_byItemId.Count);
            foreach (var pair in _byItemId)
            {
                if (pair.Value.Activated)
                    list.Add(pair.Key);
            }

            return list;
        }
    }

    /// <summary>Test / future manager hook when a player legitimately acquires a cursed blade.</summary>
    public bool TryMarkOwned(int itemId, int ownerObjectId)
    {
        if (!Enabled || !CursedWeaponIds.IsCursedItem(itemId))
            return false;
        _byItemId[itemId] = new ActiveBlade(ownerObjectId, Activated: true);
        return true;
    }

    public void ClearForTest() => _byItemId.Clear();

    public static bool InventoryHasCursedWeapon(PlayerInventory? inventory)
    {
        if (inventory == null)
            return false;
        foreach (int itemId in CursedWeaponIds.All)
        {
            if (inventory.CountOf(itemId) > 0)
                return true;
        }

        return false;
    }

    readonly record struct ActiveBlade(int OwnerObjectId, bool Activated);
}