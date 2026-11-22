using System.Diagnostics;
using System.Threading.Channels;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Encodes and delivers sealed world fan-out batches off the simulation thread: one worker preserves global batch
/// order; each batch's sends run sequentially. <see cref="RunSynchronouslyAfterPending"/> is for paths that must stay
/// strictly after prior batches (immediate handler fan-out).
/// </summary>
internal sealed class FanoutDispatcher : IDisposable
{
    readonly Channel<SealedWorldFanoutBatch> _queue = Channel.CreateUnbounded<SealedWorldFanoutBatch>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    readonly object _gate = new();
    readonly Task _worker;
    readonly CancellationTokenSource _cts = new();
    int _closed;
    ManualResetEventSlim _idle = new(true);

    public FanoutDispatcher()
    {
        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>Non-blocking. Returns false when the dispatcher is closed.</summary>
    public bool TryEnqueue(SealedWorldFanoutBatch batch)
    {
        if (Volatile.Read(ref _closed) != 0)
            return false;
        _idle.Reset();
        return _queue.Writer.TryWrite(batch);
    }

    /// <summary>Runs <paramref name="send"/> after every batch already queued finishes (blocks the caller).</summary>
    public void RunSynchronouslyAfterPending(Action send)
    {
        ArgumentNullException.ThrowIfNull(send);
        lock (_gate)
        {
            DrainPendingLocked();
            send();
        }
    }

    /// <summary>Test and shutdown: process every queued batch on the calling thread.</summary>
    public void DrainPendingSync()
    {
        lock (_gate)
            DrainPendingLocked();
    }

    void DrainPendingLocked()
    {
        while (_queue.Reader.TryRead(out var batch))
            ExecuteBatch(batch);
        _idle.Set();
    }

    async Task ProcessAsync()
    {
        try
        {
            await foreach (var batch in _queue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                lock (_gate)
                    ExecuteBatch(batch);
                if (!_queue.Reader.TryPeek(out _))
                    _idle.Set();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void ExecuteBatch(SealedWorldFanoutBatch batch)
    {
        long started = Stopwatch.GetTimestamp();
        foreach (var (name, send) in batch.Sends())
        {
            try
            {
                send();
            }
            catch (Exception ex)
            {
                FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "fanout"));
                ZoneLog.Warn($"world fan-out {name} failed: {ex}");
            }
        }

        double drainMs = ElapsedMs(started);
        FineageTelemetry.TickStepFanoutDrainMilliseconds.Record(drainMs);
        if (batch.EnqueuedAtTimestamp > 0)
        {
            double wireMs = ElapsedMs(batch.EnqueuedAtTimestamp);
            FineageTelemetry.CommandToWireMilliseconds.Record(wireMs);
        }
    }

    static double ElapsedMs(long fromTimestamp) =>
        (Stopwatch.GetTimestamp() - fromTimestamp) * 1000.0 / Stopwatch.Frequency;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;
        _queue.Writer.TryComplete();
        _cts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(5)); } catch { /* ignore */ }
        DrainPendingSync();
        _cts.Dispose();
        _idle.Dispose();
    }
}