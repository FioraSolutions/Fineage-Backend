using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// Issue #867 PR2: one bounded queue and consumer per connection stripe (ConnectionId % N). Each stripe owns delivery
/// order for its connections. Producers use <see cref="TryWrite"/> only: the simulation thread and the fan-out
/// dispatcher never block on a full stripe or a full mailbox.
/// </summary>
public sealed class NetFanoutStripes : IAsyncDisposable
{
    readonly Stripe[] _stripes;
    readonly CancellationTokenSource _cts = new();
    int _closed;

    public NetFanoutStripes(int? stripeCount = null, int? queueCapacity = null)
    {
        int n = stripeCount ?? NetFanoutStripeSettings.StripeCount;
        int capacity = queueCapacity ?? NetFanoutStripeSettings.QueueCapacityPerStripe;
        _stripes = new Stripe[n];
        for (int i = 0; i < n; i++)
            _stripes[i] = new Stripe(i, capacity, ProcessStripeAsync);
    }

    public int StripeCount => _stripes.Length;

    public void EnqueueTrySend(ConnectionHandle handle, ushort opcode, byte[] payload, long mailboxAtTimestamp = 0) =>
        Enqueue(handle, new WorkItem(handle, [new OutboundFrame(opcode, payload, mailboxAtTimestamp)], Snapshot: false));

    public void EnqueueCritical(ConnectionHandle handle, ushort opcode, byte[] payload, long mailboxAtTimestamp = 0) =>
        Enqueue(handle, new WorkItem(handle, [new OutboundFrame(opcode, payload, mailboxAtTimestamp)], Snapshot: false));

    /// <summary>One ordered batch for <paramref name="handle"/>, already grouped by the delivery scope or a stripe drain.</summary>
    internal void EnqueueBatch(ConnectionHandle handle, List<OutboundFrame> frames)
    {
        if (frames.Count == 0)
            return;
        Enqueue(handle, new WorkItem(handle, frames.ToArray(), Snapshot: false));
    }

    /// <summary>One knownlist / appearance snapshot. A newer pending snapshot replaces the previous one.</summary>
    internal void EnqueueSnapshot(ConnectionHandle handle, List<OutboundFrame> frames)
    {
        if (frames.Count == 0)
            return;
        Enqueue(handle, new WorkItem(handle, frames.ToArray(), Snapshot: true));
    }

    void Enqueue(ConnectionHandle handle, WorkItem item)
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            NetHostLog.Error(
                $"fan-out stripes are closed; rejected {item.Frames.Length} frames for objectId={handle.ObjectId} connectionId={handle.Connection.ConnectionId}");
            return;
        }

        int stripe = StripeIndex(handle.Connection.ConnectionId);
        long waitStarted = Stopwatch.GetTimestamp();
        if (!_stripes[stripe].Writer.TryWrite(item))
        {
            OutboundLane lane = item.Frames.Length == 0
                ? OutboundLane.Reliable
                : OutboundLanePolicy.Classify(item.Frames[0].Opcode);
            OutboundLaneMetrics.Blocked(lane, item.Frames.Length);
            NetHostLog.Error(
                $"fan-out stripe {stripe} queue is full; rejected {item.Frames.Length} frames for objectId={handle.ObjectId} connectionId={handle.Connection.ConnectionId}");
            if (ContainsReliable(item.Frames))
                handle.Connection.FailOutbound($"fan-out stripe {stripe} queue is full ({item.Frames.Length} frames)");
            else
                OutboundLaneMetrics.Full(lane, item.Frames.Length);
            return;
        }

        double waitMs = (Stopwatch.GetTimestamp() - waitStarted) * 1000.0 / Stopwatch.Frequency;
        FineageTelemetry.FanoutStripeEnqueueWaitMilliseconds.Record(
            waitMs,
            new KeyValuePair<string, object?>("stripe", stripe));
    }

    static bool ContainsReliable(OutboundFrame[] frames)
    {
        for (int i = 0; i < frames.Length; i++)
        {
            if (OutboundLanePolicy.IsReliable(frames[i].Opcode))
                return true;
        }

        return false;
    }

    int StripeIndex(ulong connectionId) => (int)(connectionId % (ulong)_stripes.Length);

    async Task ProcessStripeAsync(Stripe stripe)
    {
        var pending = new List<WorkItem>(8);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!await stripe.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                    break;

                pending.Clear();
                while (stripe.Reader.TryRead(out WorkItem item))
                    pending.Add(item);

                DeliverDrain(pending);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    static void DeliverDrain(List<WorkItem> pending)
    {
        if (pending.Count == 0)
            return;

        // One submission per connection, frames in the order the stripe observed them.
        // The generation check is per item: a stale handle drained in the same cycle as the
        // rebound connection must not discard the live frames.
        var groups = new Dictionary<GroupKey, List<OutboundFrame>>(pending.Count);
        var order = new List<GroupKey>(pending.Count);
        foreach (WorkItem item in pending)
        {
            NetConnection connection = item.Handle.Connection;
            if (!connection.IsBoundTo(item.Handle))
            {
                NetHostLog.Error(
                    $"stale connection handle objectId={item.Handle.ObjectId} generation={item.Handle.EntityGeneration} connectionId={connection.ConnectionId}: live binding does not match");
                continue;
            }

            var key = new GroupKey(connection, item.Snapshot);
            if (!groups.TryGetValue(key, out List<OutboundFrame>? frames))
            {
                frames = new List<OutboundFrame>(item.Frames.Length);
                groups.Add(key, frames);
                order.Add(key);
            }

            OutboundFrame[] batch = item.Frames;
            for (int i = 0; i < batch.Length; i++)
                frames.Add(batch[i]);
        }

        foreach (GroupKey key in order)
        {
            if (key.Snapshot)
                key.Connection.SubmitSnapshot(groups[key]);
            else
                key.Connection.SubmitOrderedBatch(groups[key]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        foreach (Stripe stripe in _stripes)
            stripe.Writer.TryComplete();
        _cts.Cancel();
        await Task.WhenAll(_stripes.Select(s => s.Worker)).ConfigureAwait(false);
        _cts.Dispose();
    }

    readonly record struct GroupKey(NetConnection Connection, bool Snapshot);

    readonly record struct WorkItem(ConnectionHandle Handle, OutboundFrame[] Frames, bool Snapshot);

    sealed class Stripe
    {
        public Channel<WorkItem> Channel { get; }
        public ChannelWriter<WorkItem> Writer => Channel.Writer;
        public ChannelReader<WorkItem> Reader => Channel.Reader;
        public Task Worker { get; }

        public Stripe(int index, int capacity, Func<Stripe, Task> loop)
        {
            Channel = System.Threading.Channels.Channel.CreateBounded<WorkItem>(
                new BoundedChannelOptions(capacity)
                {
                    // Producers TryWrite and apply the slow-client policy. Wait mode stays so a mistaken
                    // WriteAsync cannot drop frames silently; nothing on the tick calls it.
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
            Worker = Task.Run(() => loop(this));
        }
    }
}
