namespace Fineage.Game;

/// <summary>
/// Hands out item instance ids. One allocator per zone keeps every player's items distinct.
/// Ids start at <see cref="PlayerInventory.FirstObjectId"/>, above character ids and
/// <see cref="NpcObjectIdAllocator"/> (legacy NPCs from 50000, town NPCs from 60000).
/// <para>
/// Block mode (<see cref="ItemObjectIdAllocator(Func{int, int}, int, int)"/>): ids are taken from contiguous blocks
/// reserved on the shared account store, so login and every zone process allocate disjoint ids. The next block is
/// prefetched off the calling thread once half of the current one is used, so the simulation thread does not wait
/// on the store in steady state.
/// </para>
/// </summary>
public sealed class ItemObjectIdAllocator
{
    readonly object _gate = new();
    readonly Func<int, int>? _reserveBlock;
    readonly int _blockSize;
    int _next;
    int _blockEnd = int.MaxValue;
    Task<int>? _prefetch;

    public ItemObjectIdAllocator(int first = PlayerInventory.FirstObjectId)
    {
        if (first <= 0)
            throw new ArgumentOutOfRangeException(nameof(first), first, "Object ids must be positive.");
        _next = first;
    }

    /// <param name="reserveBlock">Reserves <c>count</c> contiguous ids on the shared store and returns the first.</param>
    /// <param name="blockSize">Ids per reservation.</param>
    /// <param name="floor">Never hand out an id below this (e.g. the economy ledger's highest instance id + 1).</param>
    public ItemObjectIdAllocator(Func<int, int> reserveBlock, int blockSize, int floor = PlayerInventory.FirstObjectId)
    {
        ArgumentNullException.ThrowIfNull(reserveBlock);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 2);
        _reserveBlock = reserveBlock;
        _blockSize = blockSize;
        int first = reserveBlock(blockSize);
        int end = checked(first + blockSize);
        while (end <= floor)
        {
            first = reserveBlock(blockSize);
            end = checked(first + blockSize);
        }

        _next = Math.Max(first, floor);
        _blockEnd = end;
    }

    /// <summary>Blocks the caller had to wait for because the prefetch had not completed (should stay 0).</summary>
    public int BlockingRefills { get; private set; }

    public int Next()
    {
        lock (_gate)
        {
            if (_reserveBlock != null && _next >= _blockEnd)
                TakeNextBlockLocked();
            int id = _next;
            _next = checked(_next + 1);
            if (_reserveBlock != null && _prefetch == null && _blockEnd - _next <= _blockSize / 2)
            {
                var reserve = _reserveBlock;
                int size = _blockSize;
                _prefetch = Task.Run(() => reserve(size));
            }

            return id;
        }
    }

    void TakeNextBlockLocked()
    {
        var pending = _prefetch;
        _prefetch = null;
        int first;
        if (pending == null)
        {
            BlockingRefills++;
            first = _reserveBlock!(_blockSize);
        }
        else
        {
            if (!pending.IsCompleted)
                BlockingRefills++;
            first = pending.GetAwaiter().GetResult();
        }

        _next = first;
        _blockEnd = checked(first + _blockSize);
    }

    /// <summary>
    /// Raises the cursor past <paramref name="objectId"/> so later <see cref="Next"/> calls stay unique. In block
    /// mode only ids inside the current block move the cursor; ids outside it belong to other reservations.
    /// </summary>
    public void Observe(int objectId)
    {
        if (objectId <= 0)
            return;
        lock (_gate)
        {
            if (objectId >= _next && objectId < _blockEnd)
                _next = checked(objectId + 1);
        }
    }

    public int PeekNext()
    {
        lock (_gate)
            return _next;
    }
}
