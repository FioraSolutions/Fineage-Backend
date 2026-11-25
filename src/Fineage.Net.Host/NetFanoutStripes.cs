using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// Issue #867 PR2: one bounded queue and consumer per connection stripe (ConnectionId % N). Each stripe owns delivery
/// order for its connections. Producers use <see cref="TryWrite"/> only: the simulation thread and the fan-out
/// dispatcher never block on a full stripe or a full mailbox.
/// <para>
/// Each stripe consumer is a dedicated thread woken by <see cref="WorkerWakeup"/>, so per-connection delivery runs
/// in parallel across stripes and never waits for a thread-pool slot behind the socket pumps.
/// </para>
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
        NetFanoutStripeSettings.Validate(n, capacity);
        _stripes = new Stripe[n];
        for (int i = 0; i < n; i++)
            _stripes[i] = new Stripe(i, capacity, ProcessStripe);
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
            if (ContainsMustDeliver(item.Frames))
                handle.Connection.FailOutbound($"fan-out stripe {stripe} queue is full ({item.Frames.Length} frames)");
            else
                OutboundLaneMetrics.Full(lane, item.Frames.Length);
            return;
        }

        _stripes[stripe].Wakeup.Signal();
        double waitMs = (Stopwatch.GetTimestamp() - waitStarted) * 1000.0 / Stopwatch.Frequency;
        FineageTelemetry.FanoutStripeEnqueueWaitMilliseconds.Record(
            waitMs,
            new KeyValuePair<string, object?>("stripe", stripe));
    }

    static bool ContainsMustDeliver(OutboundFrame[] frames)
    {
        for (int i = 0; i < frames.Length; i++)
        {
            if (OutboundLanePolicy.MustDeliver(frames[i].Opcode))
                return true;
        }

        return false;
    }

    int StripeIndex(ulong connectionId) => (int)(connectionId % (ulong)_stripes.Length);

    void ProcessStripe(Stripe stripe)
    {
        var pending = new List<WorkItem>(8);
        ChannelReader<WorkItem> reader = stripe.Reader;
        while (!_cts.IsCancellationRequested)
        {
            pending.Clear();
            while (reader.TryRead(out WorkItem item))
                pending.Add(item);

            if (pending.Count > 0)
            {
                DeliverDrain(pending);
                continue;
            }

            if (reader.Completion.IsCompleted || stripe.Wakeup.Stopped)
                break;
            stripe.Wakeup.Park(() => reader.Count > 0);
        }
    }

    static void DeliverDrain(List<WorkItem> pending)
    {
        if (pending.Count == 0)
            return;

        // Each work item is one caller batch, in the order the stripe observed it.
        // Gluing every item for a connection into one list made an empty mailbox treat the whole
        // drain as the single over-budget burst: a slow client was closed only when this channel
        // happened to be full, which depends on the worker keeping up with the producer.
        // The generation check is per item: a stale handle drained in the same cycle as the
        // rebound connection must not discard the live frames.
        foreach (WorkItem item in pending)
        {
            NetConnection connection = item.Handle.Connection;
            if (!connection.IsBoundTo(item.Handle))
            {
                NetHostLog.Error(
                    $"stale connection handle objectId={item.Handle.ObjectId} generation={item.Handle.EntityGeneration} connectionId={connection.ConnectionId}: live binding does not match");
                continue;
            }

            var frames = new List<OutboundFrame>(item.Frames.Length);
            OutboundFrame[] batch = item.Frames;
            for (int i = 0; i < batch.Length; i++)
                frames.Add(batch[i]);

            try
            {
                if (item.Snapshot)
                    connection.SubmitSnapshot(frames);
                else
                    connection.SubmitOrderedBatch(frames);
            }
            catch (Exception ex)
            {
                // One connection's malformed batch must not kill the stripe worker for every other connection.
                NetHostLog.Error(
                    $"fan-out stripe failed to admit {frames.Count} frames for {connection.Describe()}: {ex}");
                connection.FailOutbound($"outbound admission threw {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        foreach (Stripe stripe in _stripes)
        {
            stripe.Writer.TryComplete();
            stripe.Wakeup.Stop();
        }

        // Workers drain what is queued, then exit on the completed channel.
        await Task.Run(() =>
        {
            foreach (Stripe stripe in _stripes)
            {
                if (!stripe.Worker.Join(TimeSpan.FromSeconds(10)))
                    NetHostLog.Error($"fan-out stripe {stripe.Index} worker did not stop within 10s");
            }
        }).ConfigureAwait(false);
        _cts.Cancel();
        foreach (Stripe stripe in _stripes)
            stripe.Wakeup.Dispose();
        _cts.Dispose();
    }

    readonly record struct WorkItem(ConnectionHandle Handle, OutboundFrame[] Frames, bool Snapshot);

    sealed class Stripe
    {
        public Channel<WorkItem> Channel { get; }
        public ChannelWriter<WorkItem> Writer => Channel.Writer;
        public ChannelReader<WorkItem> Reader => Channel.Reader;
        public Thread Worker { get; }
        public WorkerWakeup Wakeup { get; } = new();
        public int Index { get; }

        public Stripe(int index, int capacity, Action<Stripe> loop)
        {
            Index = index;
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
            Worker = new Thread(() => loop(this)) { IsBackground = true, Name = $"fanout-stripe-{index}" };
            Worker.Start();
        }
    }
}
