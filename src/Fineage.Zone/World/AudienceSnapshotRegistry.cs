using System.Buffers;

namespace Fineage.Zone.World;

/// <summary>Id of an immutable watcher list. Zero is the empty audience and is never stored.</summary>
internal readonly record struct AudienceSnapshotId(int Value)
{
    public bool IsEmpty => Value == 0;
}

/// <summary>
/// Live watcher audiences. An id is minted when a set is captured and released when the last batch
/// and the owning entity drop it, so the registry keeps only audiences still in use.
/// Identical live lists share one id. Slot indexes are reused after release.
/// </summary>
internal static class AudienceSnapshots
{
    struct Slot
    {
        public int[] Ids;
        public int Refs;
        public int Hash;
        public int Next;
    }

    static readonly object Gate = new();
    static readonly Dictionary<int, int> Heads = new();
    static readonly Stack<int> Free = new();
    static Slot[] _slots = new Slot[4];
    static int _high;
    static int _live;

    public static int LiveCount
    {
        get { lock (Gate) return _live; }
    }

    public static int SlotCapacity
    {
        get { lock (Gate) return _slots.Length; }
    }

    /// <summary>Highest slot index ever minted. Releasing a snapshot does not raise it again.</summary>
    public static int HighWater
    {
        get { lock (Gate) return _high; }
    }

    /// <summary>Runs <paramref name="body"/> while every other snapshot operation waits, so a test can watch slot reuse.</summary>
    public static void Exclusive(Action body)
    {
        lock (Gate)
            body();
    }

    /// <summary>
    /// A stable list captured from an entity whose knownlist has not changed since shares that entity's id.
    /// Anything else is copied (and sorted when <paramref name="sort"/> is set, so replaceable writes coalesce
    /// independent of caller order).
    /// </summary>
    /// <summary>Stores <paramref name="ids"/> as a new snapshot, or adds a reference when that sequence is already live.</summary>
    public static AudienceSnapshotId Adopt(int[] ids) => ids.Length == 0 ? default : AcquireOwned(ids);

    public static AudienceSnapshotId Capture(IReadOnlyList<int> watchers, bool sort)
    {
        if (watchers is SnapshottedWatchers snap && snap.Stable && snap.Owner != null &&
            snap.Epoch == snap.Owner.NetWatcherEpoch)
            return AddRef(snap.Snapshot);

        int n = watchers.Count;
        if (n == 0)
            return default;
        if (sort && n > 1)
            return CaptureSorted(watchers);
        return CaptureSequence(watchers);
    }

    static AudienceSnapshotId CaptureSequence(IReadOnlyList<int> watchers)
    {
        int hash = HashList(watchers);
        lock (Gate)
        {
            if (TryFindList(watchers, hash, out int existing))
            {
                _slots[existing].Refs++;
                return new AudienceSnapshotId(existing);
            }
        }

        var owned = new int[watchers.Count];
        for (int i = 0; i < owned.Length; i++)
            owned[i] = watchers[i];
        return AcquireOwned(owned);
    }

    static AudienceSnapshotId CaptureSorted(IReadOnlyList<int> watchers)
    {
        int n = watchers.Count;
        int[] tmp = ArrayPool<int>.Shared.Rent(n);
        try
        {
            for (int i = 0; i < n; i++)
                tmp[i] = watchers[i];
            Array.Sort(tmp, 0, n);
            var span = tmp.AsSpan(0, n);
            int hash = Hash(span);
            lock (Gate)
            {
                if (TryFind(span, hash, out int existing))
                {
                    _slots[existing].Refs++;
                    return new AudienceSnapshotId(existing);
                }
            }

            return AcquireOwned(span.ToArray());
        }
        finally
        {
            ArrayPool<int>.Shared.Return(tmp);
        }
    }

    public static AudienceSnapshotId AddRef(AudienceSnapshotId id)
    {
        if (id.IsEmpty)
            return id;
        lock (Gate)
        {
            ref var slot = ref _slots[id.Value];
            if (slot.Refs <= 0)
                throw new InvalidOperationException($"audience {id.Value} is not live");
            slot.Refs++;
            return id;
        }
    }

    public static void Release(AudienceSnapshotId id)
    {
        if (id.IsEmpty)
            return;
        lock (Gate)
        {
            ref var slot = ref _slots[id.Value];
            if (slot.Refs <= 0)
                throw new InvalidOperationException($"audience {id.Value} released too many times");
            if (--slot.Refs > 0)
                return;
            Unlink(slot.Hash, id.Value);
            slot.Ids = [];
            slot.Hash = 0;
            slot.Next = 0;
            Free.Push(id.Value);
            _live--;
        }
    }

    public static int[] Watchers(AudienceSnapshotId id)
    {
        if (id.IsEmpty)
            return [];
        lock (Gate)
        {
            ref var slot = ref _slots[id.Value];
            if (slot.Refs <= 0)
                throw new InvalidOperationException($"audience {id.Value} is not live");
            return slot.Ids;
        }
    }

    static AudienceSnapshotId AcquireOwned(int[] ids)
    {
        int hash = Hash(ids);
        lock (Gate)
        {
            if (TryFind(ids, hash, out int existing))
            {
                _slots[existing].Refs++;
                return new AudienceSnapshotId(existing);
            }

            int slot = Free.Count > 0 ? Free.Pop() : ++_high;
            if (slot >= _slots.Length)
            {
                var grown = new Slot[_slots.Length * 2];
                Array.Copy(_slots, grown, _slots.Length);
                _slots = grown;
            }

            int head = Heads.GetValueOrDefault(hash);
            _slots[slot] = new Slot { Ids = ids, Refs = 1, Hash = hash, Next = head };
            Heads[hash] = slot;
            _live++;
            return new AudienceSnapshotId(slot);
        }
    }

    static bool TryFind(ReadOnlySpan<int> ids, int hash, out int slot)
    {
        if (!Heads.TryGetValue(hash, out int i))
        {
            slot = 0;
            return false;
        }

        while (i != 0)
        {
            if (_slots[i].Hash == hash && _slots[i].Ids.AsSpan().SequenceEqual(ids))
            {
                slot = i;
                return true;
            }

            i = _slots[i].Next;
        }

        slot = 0;
        return false;
    }

    static void Unlink(int hash, int slot)
    {
        int prev = 0;
        int i = Heads[hash];
        while (i != 0)
        {
            int next = _slots[i].Next;
            if (i == slot)
            {
                if (prev == 0)
                {
                    if (next == 0)
                        Heads.Remove(hash);
                    else
                        Heads[hash] = next;
                }
                else
                {
                    _slots[prev].Next = next;
                }

                return;
            }

            prev = i;
            i = next;
        }
    }

    static bool TryFindList(IReadOnlyList<int> ids, int hash, out int slot)
    {
        if (!Heads.TryGetValue(hash, out int i))
        {
            slot = 0;
            return false;
        }

        while (i != 0)
        {
            var stored = _slots[i].Ids;
            if (_slots[i].Hash == hash && stored.Length == ids.Count)
            {
                bool same = true;
                for (int n = 0; n < stored.Length; n++)
                {
                    if (stored[n] != ids[n])
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    slot = i;
                    return true;
                }
            }

            i = _slots[i].Next;
        }

        slot = 0;
        return false;
    }

    static int HashList(IReadOnlyList<int> ids)
    {
        var hash = new HashCode();
        for (int i = 0; i < ids.Count; i++)
            hash.Add(ids[i]);
        return hash.ToHashCode();
    }

    static int Hash(ReadOnlySpan<int> ids)
    {
        var hash = new HashCode();
        foreach (int id in ids)
            hash.Add(id);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Watcher list returned by <c>KnownNetPlayersOf</c>. While <see cref="Stable"/> and the owner's epoch match,
/// fan-out reuses <see cref="Snapshot"/> instead of copying. A later <see cref="Add"/> or <see cref="Remove"/>
/// records the caller's own edits and forces a fresh capture.
/// </summary>
internal sealed class SnapshottedWatchers : List<int>
{
    public ZoneEntity? Owner { get; private set; }

    public int Epoch { get; private set; }

    public AudienceSnapshotId Snapshot { get; private set; }

    public bool Stable { get; private set; }

    public void Attach(ZoneEntity owner, AudienceSnapshotId snapshot)
    {
        Owner = owner;
        Epoch = owner.NetWatcherEpoch;
        Snapshot = snapshot;
        Stable = true;
    }

    public new void Add(int item)
    {
        Stable = false;
        base.Add(item);
    }

    public new bool Remove(int item)
    {
        Stable = false;
        return base.Remove(item);
    }
}
