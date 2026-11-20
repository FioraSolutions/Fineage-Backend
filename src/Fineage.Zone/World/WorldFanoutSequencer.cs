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
    readonly object _sendLock = new();
    readonly Queue<WorldFanoutBatch> _sealed = new();
    WorldFanoutBatch? _recording;
    int _recordingThread;
    int _depth;

    /// <summary>The open batch when the calling thread is inside a recorded step, otherwise null.</summary>
    public WorldFanoutBatch? Recording
    {
        get
        {
            var batch = Volatile.Read(ref _recording);
            return batch != null && _recordingThread == Environment.CurrentManagedThreadId ? batch : null;
        }
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
