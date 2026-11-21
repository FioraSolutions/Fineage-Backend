namespace Fineage.Game.Economy;

/// <summary>What one account holds: a count per item, and the pieces of non-stackable items by instance id.</summary>
public sealed class LedgerHoldings
{
    readonly SortedDictionary<int, long> _counts = [];
    readonly SortedDictionary<int, int> _instances = [];

    public IReadOnlyDictionary<int, long> Counts => _counts;

    /// <summary>Instance id to item id.</summary>
    public IReadOnlyDictionary<int, int> Instances => _instances;

    public long CountOf(int itemId) => _counts.GetValueOrDefault(itemId);

    public bool IsEmpty => _counts.Count == 0 && _instances.Count == 0;

    public LedgerHoldings Add(int itemId, long count)
    {
        if (count == 0)
            return this;
        long next = checked(_counts.GetValueOrDefault(itemId) + count);
        if (next == 0)
            _counts.Remove(itemId);
        else
            _counts[itemId] = next;
        return this;
    }

    public LedgerHoldings AddPiece(int itemId, int instanceId)
    {
        if (!_instances.TryAdd(instanceId, itemId))
            throw new InvalidOperationException($"instance {instanceId} is held twice");
        return Add(itemId, 1);
    }

    internal void RemovePiece(int instanceId) => _instances.Remove(instanceId);

    internal bool HoldsPiecesOf(int itemId)
    {
        foreach (int held in _instances.Values)
        {
            if (held == itemId)
                return true;
        }

        return false;
    }

    public bool SameAs(LedgerHoldings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _counts.Count == other._counts.Count
            && _instances.Count == other._instances.Count
            && _counts.All(pair => other._counts.TryGetValue(pair.Key, out long count) && count == pair.Value)
            && _instances.All(pair => other._instances.TryGetValue(pair.Key, out int item) && item == pair.Value);
    }

    public LedgerHoldings Copy()
    {
        var copy = new LedgerHoldings();
        foreach (var (item, count) in _counts)
            copy._counts[item] = count;
        foreach (var (instance, item) in _instances)
            copy._instances[instance] = item;
        return copy;
    }

    public override string ToString() =>
        "{" + string.Join(", ", _counts.Select(pair => $"{pair.Key}x{pair.Value}")) + "}"
        + (_instances.Count == 0 ? "" : " pieces " + string.Join(",", _instances.Keys));
}
