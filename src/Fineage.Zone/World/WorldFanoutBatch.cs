using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Fan-out recorded during one simulation step (zone-simulation.md §3.6). Entries are struct descriptors;
/// payloads sit in a side list; watcher lists are audience ids. A replaceable write removes the earlier
/// write for the same (kind, subject, audience) and is appended at its own position, so every send keeps
/// its order relative to the ordered events around it. Nothing in the batch is a per-event closure.
/// </summary>
internal sealed class WorldFanoutBatch
{
    readonly List<FanoutDescriptor> _entries = [];
    readonly List<FanoutArgs> _args = [];
    readonly Dictionary<ReplaceKey, int> _replaceable = [];
    IWorldEventPublisher? _publisher;
    int _superseded;

    public int Count => _entries.Count - _superseded;

    /// <summary>Writes superseded so far, including those superseded inside absorbed region batches.</summary>
    public int Coalesced { get; private set; }

    public void Bind(IWorldEventPublisher publisher) => _publisher ??= publisher;

    public void AddPrepared(Action send)
    {
        _args.Add(new FanoutArgs(Payload: send));
        _entries.Add(new FanoutDescriptor(FanoutKind.Prepared, 0, default, _args.Count - 1, false));
    }

    public void AddOrdered(FanoutKind kind, AudienceSnapshotId audience, FanoutArgs args)
    {
        _args.Add(args);
        _entries.Add(new FanoutDescriptor(kind, 0, audience, _args.Count - 1, false));
    }

    public void AddReplaceable(FanoutKind kind, int subjectId, AudienceSnapshotId audience, FanoutArgs args)
    {
        var key = new ReplaceKey(kind, subjectId, audience);
        if (_replaceable.TryGetValue(key, out int previous))
        {
            var prior = _entries[previous];
            args = Merge(kind, _args[prior.PayloadIndex], args);
            _entries[previous] = prior with { Superseded = true };
            _superseded++;
            Coalesced++;
        }

        _args.Add(args);
        _replaceable[key] = _entries.Count;
        _entries.Add(new FanoutDescriptor(kind, subjectId, audience, _args.Count - 1, false));
    }

    /// <summary>
    /// Appends a region's batch as if its writes had been made here in order. Audience references are taken
    /// for the copies, then the region releases its own, so a snapshot stays live exactly while a batch still
    /// points at it.
    /// </summary>
    public void Absorb(WorldFanoutBatch region)
    {
        try
        {
            _publisher ??= region._publisher;
            foreach (var entry in region._entries)
            {
                if (entry.Superseded)
                    continue;
                var audience = AudienceSnapshots.AddRef(entry.Audience);
                var args = region._args[entry.PayloadIndex];
                if (FanoutKinds.IsReplaceable(entry.Kind))
                    AddReplaceable(entry.Kind, entry.SubjectId, audience, args);
                else
                    AddOrdered(entry.Kind, audience, args);
            }

            Coalesced += region.Coalesced;
        }
        finally
        {
            region.ReleaseAudiences();
        }
    }

    public FanoutSendCursor OpenCursor() => new(this);

    internal void ReleaseAudiences()
    {
        foreach (var entry in _entries)
            AudienceSnapshots.Release(entry.Audience);
        _entries.Clear();
        _args.Clear();
        _replaceable.Clear();
        _superseded = 0;
    }

    static FanoutArgs Merge(FanoutKind kind, FanoutArgs previous, FanoutArgs next)
    {
        if (kind != FanoutKind.StatusUpdate)
            return next;
        return next with
        {
            Payload = OutboxingWorldEventPublisher.MergeStatus(
                (StatusUpdateMsg)previous.Payload!,
                (StatusUpdateMsg)next.Payload!)
        };
    }

    readonly record struct ReplaceKey(FanoutKind Kind, int SubjectId, AudienceSnapshotId Audience);

    internal ref struct FanoutSendCursor
    {
        readonly WorldFanoutBatch _batch;
        int _index;

        public FanoutSendCursor(WorldFanoutBatch batch)
        {
            _batch = batch;
            _index = -1;
        }

        public FanoutKind Kind => _batch._entries[_index].Kind;

        public bool MoveNext()
        {
            var entries = _batch._entries;
            while (++_index < entries.Count)
            {
                if (!entries[_index].Superseded)
                    return true;
            }

            return false;
        }

        public void Send()
        {
            var entry = _batch._entries[_index];
            FanoutDispatch.Invoke(_batch._publisher, entry.Kind, entry.Audience, _batch._args[entry.PayloadIndex]);
        }
    }
}
