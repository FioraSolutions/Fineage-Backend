namespace Fineage.Zone.World;

/// <summary>Replaceable presence kinds: later writes supersede earlier ones for the same subject and audience.</summary>
internal enum PresenceCoalesceKind : byte
{
    StatusUpdate = 1,
    AbnormalStatusUpdate = 2,
    MoveState = 3,
    ChangeMoveType = 4,
}

/// <summary>
/// Fan-out recorded during one simulation step (zone-simulation.md §3.6). Entries are bound sends whose inputs were
/// captured under <c>_gate</c>; sending reads no world state. A replaceable write removes the earlier write for the
/// same (kind, subject, audience) and is appended at its own position, so every send keeps its order relative to
/// the ordered events around it.
/// </summary>
internal sealed class WorldFanoutBatch
{
    readonly List<Entry?> _entries = [];
    readonly Dictionary<ReplaceKey, int> _replaceable = [];

    public int Count => _entries.Count - Coalesced;

    public int Coalesced { get; private set; }

    public void AddOrdered(string name, Action send) => _entries.Add(new Entry(name, send, null));

    /// <param name="merge">Folds the superseded payload into the new one (StatusUpdate keeps attributes the new write omits).</param>
    public void AddReplaceable<T>(
        PresenceCoalesceKind kind, int subjectId, int[] audience, T payload, Func<T, T, T> merge, Func<T, Action> bind)
        where T : class
    {
        var key = new ReplaceKey(kind, subjectId, new AudienceKey(audience));
        if (_replaceable.TryGetValue(key, out int previous))
        {
            payload = merge((T)_entries[previous]!.Payload!, payload);
            _entries[previous] = null;
            Coalesced++;
        }

        _replaceable[key] = _entries.Count;
        _entries.Add(new Entry($"{kind} subject={subjectId}", bind(payload), payload));
    }

    public IEnumerable<(string Name, Action Send)> Sends()
    {
        foreach (var entry in _entries)
        {
            if (entry != null)
                yield return (entry.Name, entry.Send);
        }
    }

    sealed record Entry(string Name, Action Send, object? Payload);

    readonly record struct ReplaceKey(PresenceCoalesceKind Kind, int SubjectId, AudienceKey Audience);

    readonly struct AudienceKey(int[] ids) : IEquatable<AudienceKey>
    {
        readonly int[] _ids = ids;

        public bool Equals(AudienceKey other) => _ids.AsSpan().SequenceEqual(other._ids);

        public override bool Equals(object? obj) => obj is AudienceKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (int id in _ids)
                hash.Add(id);
            return hash.ToHashCode();
        }
    }
}
