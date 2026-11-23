namespace Fineage.Net.Host;

/// <summary>
/// Groups every fan-out frame produced while a sealed batch is delivered into one stripe enqueue per connection.
/// A knownlist snapshot is one enqueue as well, kept in the order the batch produced it.
/// Without this, each watcher frame is its own stripe item (issue #867 PR3).
/// </summary>
public sealed class FanoutDeliveryScope : IDisposable
{
    static readonly AsyncLocal<FanoutDeliveryScope?> Current = new();

    readonly List<Op> _ops = [];
    int _flushed;

    FanoutDeliveryScope()
    {
    }

    public static FanoutDeliveryScope Begin()
    {
        if (Current.Value != null)
            throw new InvalidOperationException("fan-out delivery scope is already active on this thread");

        var scope = new FanoutDeliveryScope();
        Current.Value = scope;
        return scope;
    }

    /// <summary>True when <paramref name="payload"/> was kept for the active scope instead of hitting the stripe queue.</summary>
    internal static bool TryBuffer(
        NetFanoutStripes stripes, ConnectionHandle handle, ushort opcode, byte[] payload, long mailboxAtTimestamp = 0)
    {
        var scope = Current.Value;
        if (scope == null || Volatile.Read(ref scope._flushed) != 0)
            return false;

        scope._ops.Add(new FrameOp(stripes, handle, opcode, payload, mailboxAtTimestamp));
        return true;
    }

    /// <summary>
    /// Keeps a knownlist / appearance burst on the active scope, in order with the frames already buffered.
    /// Returns false when no scope is active; the caller enqueues the snapshot itself.
    /// </summary>
    internal static bool TryBufferSnapshot(NetFanoutStripes stripes, ConnectionHandle handle, List<OutboundFrame> frames)
    {
        var scope = Current.Value;
        if (scope == null || Volatile.Read(ref scope._flushed) != 0 || frames.Count == 0)
            return false;

        scope._ops.Add(new SnapshotOp(stripes, handle, frames));
        return true;
    }

    /// <summary>Enqueues one ordered batch or snapshot per connection. Returns how many enqueues were posted.</summary>
    public int Flush()
    {
        if (Interlocked.Exchange(ref _flushed, 1) != 0)
            return 0;

        if (ReferenceEquals(Current.Value, this))
            Current.Value = null;

        if (_ops.Count == 0)
            return 0;

        int enqueued = 0;
        var pending = new Dictionary<ulong, PendingOrdered>();
        var pendingOrder = new List<ulong>();
        foreach (Op op in _ops)
        {
            if (op is FrameOp frame)
            {
                ulong id = frame.Handle.Connection.ConnectionId;
                if (!pending.TryGetValue(id, out PendingOrdered? group))
                {
                    group = new PendingOrdered(frame.Stripes, frame.Handle);
                    pending.Add(id, group);
                    pendingOrder.Add(id);
                }

                group.Frames.Add(new OutboundFrame(frame.Opcode, frame.Payload, frame.MailboxAtTimestamp));
                continue;
            }

            var snapshot = (SnapshotOp)op;
            enqueued += FlushPending(pending, pendingOrder, snapshot.Handle.Connection.ConnectionId);
            if (snapshot.Frames.Count == 0)
                continue;
            snapshot.Stripes.EnqueueSnapshot(snapshot.Handle, snapshot.Frames);
            enqueued++;
        }

        enqueued += FlushPending(pending, pendingOrder, onlyConnectionId: null);
        return enqueued;
    }

    public void Dispose() => Flush();

    static int FlushPending(Dictionary<ulong, PendingOrdered> pending, List<ulong> order, ulong? onlyConnectionId)
    {
        int enqueued = 0;
        for (int i = 0; i < order.Count; i++)
        {
            ulong id = order[i];
            if (onlyConnectionId is { } only && id != only)
                continue;
            if (!pending.TryGetValue(id, out PendingOrdered? group))
                continue;
            if (group.Frames.Count > 0)
            {
                group.Stripes.EnqueueBatch(group.Handle, group.Frames);
                enqueued++;
            }

            pending.Remove(id);
        }

        if (onlyConnectionId == null)
            order.Clear();
        else
            order.Remove(onlyConnectionId.Value);
        return enqueued;
    }

    abstract record Op;

    sealed record FrameOp(
        NetFanoutStripes Stripes, ConnectionHandle Handle, ushort Opcode, byte[] Payload, long MailboxAtTimestamp) : Op;

    sealed record SnapshotOp(NetFanoutStripes Stripes, ConnectionHandle Handle, List<OutboundFrame> Frames) : Op;

    sealed class PendingOrdered(NetFanoutStripes stripes, ConnectionHandle handle)
    {
        public NetFanoutStripes Stripes { get; } = stripes;
        public ConnectionHandle Handle { get; } = handle;
        public List<OutboundFrame> Frames { get; } = [];
    }
}
