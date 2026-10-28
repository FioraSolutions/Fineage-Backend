namespace Fineage.Game;

/// <summary>One stack or one piece in a player's inventory.</summary>
public sealed class ItemInstance
{
    internal ItemInstance(int objectId, ItemTemplate template, int count)
    {
        ObjectId = objectId;
        Template = template;
        ItemId = template.Id;
        Count = count;
    }

    public int ObjectId { get; }

    public int ItemId { get; }

    public ItemTemplate Template { get; }

    public int Count { get; internal set; }

    /// <summary>Paperdoll slot while worn. Null while the item is only in the bag.</summary>
    public PaperdollSlot? EquippedSlot { get; internal set; }

    public bool IsEquipped => EquippedSlot.HasValue;
}
