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

    int _superseded;

    public int Count => _entries.Count - _superseded;

    /// <summary>Writes superseded so far, including those superseded inside absorbed region batches.</summary>
    public int Coalesced { get; private set; }

    public void AddOrdered(string name, Action send) => _entries.Add(new Entry(name, send, null, null));

    /// <param name="merge">Folds the superseded payload into the new one (StatusUpdate keeps attributes the new write omits).</param>
    public void AddReplaceable<T>(
        PresenceCoalesceKind kind, int subjectId, int[] audience, T payload, Func<T, T, T> merge, Func<T, Action> bind)
        where T : class =>
        AddReplaceable(payload, new Replace(new ReplaceKey(kind, subjectId, new AudienceKey(audience)),
            (older, newer) => merge((T)older, (T)newer), latest => bind((T)latest)));

    /// <summary>
    /// Appends a region's batch as if its writes had been made here in order: ordered sends keep their position and
    /// replaceable writes coalesce with this batch's. The merges are associative, so the result does not depend on
    /// whether a region recorded on its own first.
    /// </summary>
    public void Absorb(WorldFanoutBatch region)
    {
        foreach (var entry in region._entries)
        {
            if (entry == null)
                continue;
            if (entry.Replace is { } replace)
                AddReplaceable(entry.Payload!, replace);
            else
                _entries.Add(entry);
        }

        Coalesced += region.Coalesced;
    }

    void AddReplaceable(object payload, Replace replace)
    {
        var key = replace.Key;
        if (_replaceable.TryGetValue(key, out int previous))
        {
            payload = replace.Merge(_entries[previous]!.Payload!, payload);
            _entries[previous] = null;
            _superseded++;
            Coalesced++;
        }

        _replaceable[key] = _entries.Count;
        _entries.Add(new Entry($"{key.Kind} subject={key.SubjectId}", replace.Bind(payload), payload, replace));
    }

    public IEnumerable<(string Name, Action Send)> Sends()
    {
        foreach (var entry in _entries)
        {
            if (entry != null)
                yield return (entry.Name, entry.Send);
        }
    }

    sealed record Entry(string Name, Action Send, object? Payload, Replace? Replace);

    sealed record Replace(ReplaceKey Key, Func<object, object, object> Merge, Func<object, Action> Bind);

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
