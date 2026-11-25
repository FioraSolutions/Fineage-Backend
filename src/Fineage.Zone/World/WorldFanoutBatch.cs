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
    /// <summary>Recipients one sealed batch may carry before its oldest visual writes are shed.</summary>
    public const int DefaultSoftSendWork = 200_000;

    readonly List<FanoutDescriptor> _entries = [];
    readonly List<FanoutArgs> _args = [];
    readonly Dictionary<ReplaceKey, int> _replaceable = [];
    List<int>? _visuals;
    IWorldEventPublisher? _publisher;
    int _superseded;
    int _droppedVisuals;
    // Already reported to the dispatcher; a second report of the same shed would double-count the metric.
    int _droppedReported;

    public WorldFanoutBatch(int softSendWork = DefaultSoftSendWork)
    {
        if (softSendWork < 1)
            throw new ArgumentOutOfRangeException(nameof(softSendWork), softSendWork, "fan-out send-work budget must be positive");
        SoftSendWork = softSendWork;
    }

    public int Count => _entries.Count - _superseded - _droppedVisuals;

    /// <summary>
    /// Work this batch will hand to the delivery worker: one unit per recipient of a descriptor event and one per
    /// prepared send (whose audience is inside the closure). It is the memory/CPU size of the hand-off, because a
    /// batch of 100k recipients costs far more than a batch of two. Bounded per batch by <see cref="SoftSendWork"/>.
    /// </summary>
    public long SendWork { get; private set; }

    public int SoftSendWork { get; }

    /// <summary>Visual events shed inside this batch to keep <see cref="SendWork"/> inside the budget.</summary>
    public int DroppedVisuals => _droppedVisuals;

    /// <summary>Writes superseded so far, including those superseded inside absorbed region batches.</summary>
    public int Coalesced { get; private set; }

    public void Bind(IWorldEventPublisher publisher) => _publisher ??= publisher;

    public void AddPrepared(Action send)
    {
        _args.Add(new FanoutArgs(Payload: send));
        _entries.Add(new FanoutDescriptor(FanoutKind.Prepared, 0, default, _args.Count - 1, false));
        SendWork += 1;
    }

    public void AddOrdered(FanoutKind kind, AudienceSnapshotId audience, FanoutArgs args)
    {
        _args.Add(args);
        int index = _entries.Count;
        _entries.Add(new FanoutDescriptor(kind, 0, audience, _args.Count - 1, false));
        int work = (int)WorkOf(audience);
        SendWork += work;
        if (!FanoutKinds.IsVisual(kind))
            return;
        (_visuals ??= []).Add(index);
        ShedVisualsTo(SoftSendWork, keepIndex: index);
        _ = work;
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
            SendWork -= WorkOf(prior.Audience);
        }

        _args.Add(args);
        _replaceable[key] = _entries.Count;
        _entries.Add(new FanoutDescriptor(kind, subjectId, audience, _args.Count - 1, false));
        SendWork += WorkOf(audience);
    }

    /// <summary>
    /// Sheds the oldest visual writes until the batch fits <paramref name="limit"/> again. Visual frames are the one
    /// class the outbound mailbox may drop for a slow client anyway, so losing the oldest here costs the same picture
    /// and keeps the reliable gameplay writes. The newest visual is kept: it is the one the client is waiting for.
    /// </summary>
    void ShedVisualsTo(long limit, int keepIndex = -1)
    {
        if (SendWork <= limit || _visuals is null)
            return;
        for (int i = 0; i < _visuals.Count;)
        {
            int index = _visuals[i];
            if (index == keepIndex)
            {
                i++;
                continue;
            }

            var entry = _entries[index];
            if (entry.Dropped)
            {
                _visuals.RemoveAt(i);
                continue;
            }

            var audience = entry.Audience;
            _entries[index] = entry with { Dropped = true };
            _droppedVisuals++;
            SendWork -= WorkOf(audience);
            _visuals.RemoveAt(i);
            if (SendWork <= limit)
                return;
        }

        // A single newest visual larger than the whole budget still goes out: dropping the frame the client is
        // waiting for would hide the action entirely. The batch is then over budget, which the seal reports.
    }

    /// <summary>
    /// Sheds visual writes until the batch carries at most <paramref name="limit"/> send work. The dispatcher calls
    /// this on an enqueue that would cross the hard backlog: the hand-off then carries less work instead of growing
    /// the backlog. Returns how many writes it shed.
    /// </summary>
    internal int ShedVisualsToBudget(long limit)
    {
        int before = _droppedVisuals;
        // Overflow sheds oldest first and may take the newest visual too: the hand-off has to fit, and a lost
        // animation costs less than a gameplay write stuck behind a backlog that never drains.
        ShedVisualsTo(limit);
        return _droppedVisuals - before;
    }

    /// <summary>
    /// True when every write left in the batch is a visual one. Such a batch may be dropped whole at overflow: the
    /// client loses only animations and markers, which the outbound mailbox already sheds for a slow client.
    /// </summary>
    internal bool HasOnlyVisualWrites()
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (entry.Superseded || entry.Dropped)
                continue;
            if (!FanoutKinds.IsVisual(entry.Kind))
                return false;
        }

        return true;
    }

    long WorkOf(AudienceSnapshotId audience) => audience.IsEmpty ? 1 : AudienceSnapshots.CountOf(audience);

    /// <summary>Visual writes shed inside this batch before delivery.</summary>
    internal int TakeDroppedVisuals()
    {
        int dropped = _droppedVisuals - _droppedReported;
        _droppedReported = _droppedVisuals;
        return dropped;
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
                if (entry.Superseded || entry.Dropped)
                    continue;
                var audience = AudienceSnapshots.AddRef(entry.Audience);
                var args = region._args[entry.PayloadIndex];
                if (FanoutKinds.IsReplaceable(entry.Kind))
                    AddReplaceable(entry.Kind, entry.SubjectId, audience, args);
                else
                    AddOrdered(entry.Kind, audience, args);
            }

            Coalesced += region.Coalesced;
            _droppedVisuals += region.TakeDroppedVisuals();
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
        _visuals?.Clear();
        _superseded = 0;
        _droppedVisuals = 0;
        _droppedReported = 0;
        SendWork = 0;
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
                if (!entries[_index].Superseded && !entries[_index].Dropped)
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
