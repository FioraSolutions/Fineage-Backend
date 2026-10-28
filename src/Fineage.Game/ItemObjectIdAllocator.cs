namespace Fineage.Game;

/// <summary>
/// Hands out item instance ids. One allocator per zone keeps every player's items distinct.
/// Ids start at <see cref="PlayerInventory.FirstObjectId"/>, above character ids and Talking Island NPCs.
/// </summary>
public sealed class ItemObjectIdAllocator
{
    readonly object _gate = new();
    int _next;

    public ItemObjectIdAllocator(int first = PlayerInventory.FirstObjectId)
    {
        if (first <= 0)
            throw new ArgumentOutOfRangeException(nameof(first), first, "Object ids must be positive.");
        _next = first;
    }

    public int Next()
    {
        lock (_gate)
        {
            int id = _next;
            _next = checked(_next + 1);
            return id;
        }
    }
}
