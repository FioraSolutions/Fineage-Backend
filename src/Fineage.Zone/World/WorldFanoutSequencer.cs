using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Keeps world fan-out in <c>_gate</c> order once simulation steps send after unlock. A step records into a batch
/// under the gate and seals it into the queue before releasing; every send, batched or immediate, then goes through
/// one send lock that drains sealed batches first. A handler that emits right after a step therefore never
/// overtakes that step's traffic. Sends must not take <c>_gate</c>: a handler holding it may be waiting for the send
/// lock.
/// </summary>
internal sealed class WorldFanoutSequencer
{
    [ThreadStatic] static RegionRecording? t_region;

    readonly object _sendLock = new();
    readonly Queue<WorldFanoutBatch> _sealed = new();
    WorldFanoutBatch? _recording;
    int _recordingThread;
    int _depth;

    /// <summary>
    /// The open batch when the calling thread is inside a recorded step, otherwise null. Inside a region body that is
    /// the region's own batch (<see cref="RecordRegion"/>), on whichever thread the body runs.
    /// </summary>
    public WorldFanoutBatch? Recording
    {
        get
        {
            if (t_region is { } region && ReferenceEquals(region.Owner, this))
                return region.Batch;
            var batch = Volatile.Read(ref _recording);
            return batch != null && _recordingThread == Environment.CurrentManagedThreadId ? batch : null;
        }
    }

    /// <summary>
    /// For the duration of a region body on the calling thread: fan-out records into <paramref name="batch"/>, which
    /// the scheduler thread folds into the step's batch at the barrier (<see cref="AbsorbRegion"/>).
    /// </summary>
    public IDisposable RecordRegion(WorldFanoutBatch batch)
    {
        if (t_region != null)
            throw new InvalidOperationException("a region body is already recording fan-out on this thread");
        var recording = new RegionRecording(this, batch);
        t_region = recording;
        return recording;
    }

    /// <summary>On the scheduler thread at the barrier: appends a region's fan-out to the step's batch.</summary>
    public void AbsorbRegion(WorldFanoutBatch region)
    {
        if (region.Count == 0)
            return;
        var step = Recording ??
                   throw new InvalidOperationException("region fan-out committed outside a recorded step; region stages run under RunUnderGate");
        step.Absorb(region);
    }

    /// <summary>Under <c>_gate</c>. Nested steps join the outer batch.</summary>
    public void Begin()
    {
        if (_depth++ > 0)
            return;
        _recordingThread = Environment.CurrentManagedThreadId;
        Volatile.Write(ref _recording, new WorldFanoutBatch());
    }

    /// <summary>Under <c>_gate</c>, before releasing it: queues the batch behind earlier ones.</summary>
    public void Seal()
    {
        if (_depth == 0)
            throw new InvalidOperationException("world fan-out batch sealed without a matching Begin");
        if (--_depth > 0)
            return;
        var batch = _recording!;
        Volatile.Write(ref _recording, null);
        lock (_sealed)
            _sealed.Enqueue(batch);
    }

    /// <summary>After releasing <c>_gate</c>: sends every sealed batch in queue order.</summary>
    public void Drain()
    {
        lock (_sendLock)
            DrainLocked();
    }

    /// <summary>
    /// A send outside a recorded step (handlers, admin): runs after every sealed batch. Its own exception reaches the
    /// caller, as before batching.
    /// </summary>
    public void SendNow(Action send)
    {
        lock (_sendLock)
        {
            DrainLocked();
            send();
        }
    }

    sealed class RegionRecording(WorldFanoutSequencer owner, WorldFanoutBatch batch) : IDisposable
    {
        public WorldFanoutSequencer Owner { get; } = owner;

        public WorldFanoutBatch Batch { get; } = batch;

        public void Dispose()
        {
            if (!ReferenceEquals(t_region, this))
                throw new InvalidOperationException("region fan-out recording closed out of order");
            t_region = null;
        }
    }

    void DrainLocked()
    {
        while (true)
        {
            WorldFanoutBatch batch;
            lock (_sealed)
            {
                if (!_sealed.TryDequeue(out batch!))
                    return;
            }

            foreach (var (name, send) in batch.Sends())
            {
                try
                {
                    send();
                }
                catch (Exception ex)
                {
                    // One failed frame must not drop the rest of the step's traffic, like stage isolation.
                    FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "fanout"));
                    ZoneLog.Warn($"world fan-out {name} failed: {ex}");
                }
            }
        }
    }
}
