namespace Fineage.Zone.Interest;

/// <summary>
/// Circular interest by world distance. Used for TI scale experiments before a grid AOI.
/// </summary>
public sealed class RangeAoi(int range) : IAoi
{
    /// <summary>Extra radius before a known object is forgotten (Mobius watch/forget gap).</summary>
    public const int ForgetSlack = 500;

    readonly long _range2 = (long)range * range;
    readonly long _forget2 = (long)(range + ForgetSlack) * (range + ForgetSlack);

    public int Range => range;
    readonly Dictionary<int, (int X, int Y)> _pos = [];

    public void Enter(int objectId, int x, int y) => _pos[objectId] = (x, y);

    public void Move(int objectId, int x, int y) => _pos[objectId] = (x, y);

    public void Leave(int objectId) => _pos.Remove(objectId);

    public IReadOnlyCollection<int> GetVisible(int objectId)
    {
        if (!_pos.TryGetValue(objectId, out var me))
            return [];

        var visible = new List<int>();
        foreach (var (id, pos) in _pos)
        {
            if (id == objectId)
                continue;
            long dx = me.X - (long)pos.X;
            long dy = me.Y - (long)pos.Y;
            if (dx * dx + dy * dy <= _range2)
                visible.Add(id);
        }

        return visible;
    }

    public bool StillKnown(int objectId, int otherId)
    {
        if (!_pos.TryGetValue(objectId, out var me) || !_pos.TryGetValue(otherId, out var other))
            return false;
        long dx = me.X - (long)other.X;
        long dy = me.Y - (long)other.Y;
        return dx * dx + dy * dy <= _forget2;
    }
}
