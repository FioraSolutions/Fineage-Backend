namespace Fineage.Game;

/// <summary>
/// Object ids for world NPCs. Character ids stay below <see cref="LegacyFirst"/>.
/// Legacy spawn rows (Cedric gremlins, the dormant village-road pair) use
/// [<see cref="LegacyFirst"/>, <see cref="First"/>). Town rows allocate
/// [<see cref="First"/>, <see cref="ExclusiveEnd"/>). Item instances start at
/// <see cref="ExclusiveEnd"/>, which is <see cref="PlayerInventory.FirstObjectId"/>.
/// </summary>
public sealed class NpcObjectIdAllocator
{
    public const int LegacyFirst = 50_000;
    public const int First = 60_000;
    public const int ExclusiveEnd = PlayerInventory.FirstObjectId;

    readonly object _gate = new();
    readonly HashSet<int> _used = [];
    int _next = First;

    /// <summary>Keeps an id that the datapack already chose. Allowed in the legacy or town band.</summary>
    public void Reserve(int objectId)
    {
        lock (_gate)
        {
            if (objectId < LegacyFirst || objectId >= ExclusiveEnd)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(objectId),
                    objectId,
                    $"NPC object ids must be in [{LegacyFirst}, {ExclusiveEnd}).");
            }

            if (!_used.Add(objectId))
                throw new InvalidOperationException($"Duplicate NPC object id {objectId}.");
        }
    }

    /// <summary>Next free town-band id. Skips ids already reserved.</summary>
    public int Next()
    {
        lock (_gate)
        {
            while (_next < ExclusiveEnd)
            {
                int id = _next++;
                if (_used.Add(id))
                    return id;
            }

            throw new InvalidOperationException("NPC object id range is exhausted.");
        }
    }
}
