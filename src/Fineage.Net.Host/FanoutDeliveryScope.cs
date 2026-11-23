namespace Fineage.Net.Host;

/// <summary>
/// Groups every fan-out frame produced while a sealed batch is delivered into one stripe enqueue per connection.
/// Without this, each watcher frame is its own stripe item and the pump writes one socket buffer per frame
/// whenever it keeps up with the dispatcher (issue #867 PR3).
/// </summary>
public sealed class FanoutDeliveryScope : IDisposable
{
    static readonly AsyncLocal<FanoutDeliveryScope?> Current = new();

    readonly List<Buffered> _frames = [];
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
    internal static bool TryBuffer(NetFanoutStripes stripes, ConnectionHandle handle, ushort opcode, byte[] payload)
    {
        var scope = Current.Value;
        if (scope == null || Volatile.Read(ref scope._flushed) != 0)
            return false;

        scope._frames.Add(new Buffered(stripes, handle, opcode, payload));
        return true;
    }

    /// <summary>Enqueues one ordered batch per connection. Returns how many connections were enqueued.</summary>
    public int Flush()
    {
        if (Interlocked.Exchange(ref _flushed, 1) != 0)
            return 0;

        if (ReferenceEquals(Current.Value, this))
            Current.Value = null;

        if (_frames.Count == 0)
            return 0;

        var groups = new Dictionary<GroupKey, Group>(_frames.Count);
        var order = new List<GroupKey>();
        foreach (Buffered frame in _frames)
        {
            var key = new GroupKey(frame.Stripes, frame.Handle.Connection.ConnectionId);
            if (!groups.TryGetValue(key, out Group? group))
            {
                group = new Group(frame.Stripes, frame.Handle);
                groups.Add(key, group);
                order.Add(key);
            }

            group.Frames.Add(new OutboundFrame(frame.Opcode, frame.Payload));
        }

        foreach (GroupKey key in order)
        {
            Group group = groups[key];
            group.Stripes.EnqueueBatch(group.Handle, group.Frames);
        }

        return order.Count;
    }

    public void Dispose() => Flush();

    readonly record struct Buffered(NetFanoutStripes Stripes, ConnectionHandle Handle, ushort Opcode, byte[] Payload);

    readonly record struct GroupKey(NetFanoutStripes Stripes, ulong ConnectionId);

    sealed class Group(NetFanoutStripes stripes, ConnectionHandle handle)
    {
        public NetFanoutStripes Stripes { get; } = stripes;
        public ConnectionHandle Handle { get; } = handle;
        public List<OutboundFrame> Frames { get; } = [];
    }
}
