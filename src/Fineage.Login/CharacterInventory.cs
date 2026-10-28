using Fineage.Game;

namespace Fineage.Login;

public static class CharacterInventory
{
    public static StoredItem[] Rows(PlayerInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var rows = new StoredItem[inventory.Items.Count];
        for (int i = 0; i < rows.Length; i++)
        {
            var item = inventory.Items[i];
            rows[i] = new StoredItem(item.ItemId, item.Count, item.EquippedSlot is { } slot ? (int)slot : -1);
        }

        return rows;
    }

    public static PlayerInventory Restore(ItemCatalog catalog, ItemObjectIdAllocator ids, IReadOnlyList<StoredItem> items)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(items);
        Validate(items);
        var inventory = new PlayerInventory(catalog, ids);
        foreach (var row in items)
        {
            var instance = inventory.Add(row.ItemId, row.Count);
            if (row.EquippedSlot >= 0 && instance.Template.Equippable)
                inventory.Equip(instance);
        }

        return inventory;
    }

    public static void Validate(IReadOnlyList<StoredItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            if (item.ItemId <= 0)
                throw new ArgumentOutOfRangeException(nameof(items), $"Item id {item.ItemId} must be positive.");
            if (item.Count < 1)
                throw new ArgumentOutOfRangeException(nameof(items), $"Item {item.ItemId} count must be at least 1.");
            if (item.EquippedSlot < -1 || item.EquippedSlot > (int)PaperdollSlot.Hair2)
                throw new ArgumentOutOfRangeException(nameof(items), $"Item {item.ItemId} equipped slot {item.EquippedSlot} is outside -1..16.");
        }
    }
}
