using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// Issue #867 PR2: one bounded queue and consumer per connection stripe (ConnectionId % N). Each stripe owns delivery
/// order for its connections; fan-out no longer runs as one global serial worker.
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

    public void EnqueueTrySend(ConnectionHandle handle, ushort opcode, byte[] payload)
    {
        Enqueue(handle, new WorkItem(handle, opcode, payload, Critical: false));
    }

    public void EnqueueCritical(ConnectionHandle handle, ushort opcode, byte[] payload)
    {
        Enqueue(handle, new WorkItem(handle, opcode, payload, Critical: true));
    }

    void Enqueue(ConnectionHandle handle, WorkItem item)
    {
        if (Volatile.Read(ref _closed) != 0)
            return;
        int stripe = StripeIndex(handle.Connection.ConnectionId);
        long waitStarted = Stopwatch.GetTimestamp();
        try
        {
            _stripes[stripe].Writer.WriteAsync(item, _cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
            return;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return;
        }

        double waitMs = (Stopwatch.GetTimestamp() - waitStarted) * 1000.0 / Stopwatch.Frequency;
        FineageTelemetry.FanoutStripeEnqueueWaitMilliseconds.Record(
            waitMs,
            new KeyValuePair<string, object?>("stripe", stripe));
    }

    int StripeIndex(ulong connectionId) => (int)(connectionId % (ulong)_stripes.Length);

    async Task ProcessStripeAsync(Stripe stripe)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (!await stripe.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                    break;
                while (stripe.Reader.TryRead(out WorkItem item))
                    Dispatch(item);
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
        }
    }

    static void Dispatch(WorkItem item)
    {
        if (item.Critical)
        {
            item.Handle.Connection.PrepareMailboxBarrierSend();
            _ = item.Handle.Connection.SendAsync(item.Opcode, item.Payload);
        }
        else
            item.Handle.Connection.TrySendFromStripe(item.Opcode, item.Payload);
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

    readonly record struct WorkItem(ConnectionHandle Handle, ushort Opcode, byte[] Payload, bool Critical = false);

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
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
            Worker = Task.Run(() => loop(this));
        }
    }
}