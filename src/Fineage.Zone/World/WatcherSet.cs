namespace Fineage.Zone.World;

/// <summary>
/// An entity's knownlist. Mutations notify <see cref="Changed"/> so the net-watcher audience id is dropped
/// when the set actually changes and kept when a duplicate add or an empty clear does nothing.
/// </summary>
public sealed class WatcherSet : IReadOnlyCollection<int>, ICollection<int>
{
    readonly HashSet<int> _ids = [];

    /// <summary>Runs after an add, remove, or clear that changed membership.</summary>
    internal Action? Changed { get; set; }

    public int Count => _ids.Count;

    public bool Contains(int id) => _ids.Contains(id);

    public bool Add(int id)
    {
        if (!_ids.Add(id))
            return false;
        Changed?.Invoke();
        return true;
    }

    public bool Remove(int id)
    {
        if (!_ids.Remove(id))
            return false;
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_ids.Count == 0)
            return;
        _ids.Clear();
        Changed?.Invoke();
    }

    public HashSet<int>.Enumerator GetEnumerator() => _ids.GetEnumerator();

    IEnumerator<int> IEnumerable<int>.GetEnumerator() => _ids.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _ids.GetEnumerator();

    bool ICollection<int>.IsReadOnly => false;

    void ICollection<int>.Add(int item) => Add(item);

    void ICollection<int>.CopyTo(int[] array, int arrayIndex) => _ids.CopyTo(array, arrayIndex);
}
