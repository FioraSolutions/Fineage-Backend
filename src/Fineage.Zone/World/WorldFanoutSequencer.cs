using System.Diagnostics;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Keeps world fan-out in step order. A step records into a batch and seals it into the <see cref="FanoutDispatcher"/>
/// when its body returns; immediate sends wait until prior sealed batches are delivered.
/// </summary>
internal sealed class WorldFanoutSequencer(FanoutDispatcher dispatcher)
{
    [ThreadStatic] static RegionRecording? t_region;

    readonly FanoutDispatcher _dispatcher = dispatcher;
    WorldFanoutBatch? _recording;
    int _recordingThread;
    int _depth;
    long _tick;
    byte _phase;
    ulong _sequence;

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

    public void ConfigureSequence(long tick, byte phase)
    {
        _tick = tick;
        _phase = phase;
        _sequence = 0;
    }

    public IDisposable RecordRegion(WorldFanoutBatch batch)
    {
        if (t_region != null)
            throw new InvalidOperationException("a region body is already recording fan-out on this thread");
        var recording = new RegionRecording(this, batch);
        t_region = recording;
        return recording;
    }

    public void AbsorbRegion(WorldFanoutBatch region)
    {
        if (region.Count == 0)
            return;
        var step = Recording ??
                   throw new InvalidOperationException("region fan-out committed outside a recorded step; region stages run inside RunStep");
        step.Absorb(region);
    }

    public void Begin()
    {
        if (_depth++ > 0)
            return;
        _recordingThread = Environment.CurrentManagedThreadId;
        Volatile.Write(ref _recording, new WorldFanoutBatch());
    }

  /// <param name="commandCommitTimestamp">When &gt; 0, marks this batch for command→mailbox timing.</param>
    public void Seal(long commandCommitTimestamp = 0)
    {
        if (_depth == 0)
            throw new InvalidOperationException("world fan-out batch sealed without a matching Begin");
        if (--_depth > 0)
            return;
        var batch = _recording!;
        Volatile.Write(ref _recording, null);
        var seq = new WorldEventSequence(_tick, _phase, _sequence++);
        if (!_dispatcher.TryEnqueue(new SealedWorldFanoutBatch(batch, seq, commandCommitTimestamp)))
            throw new InvalidOperationException("fan-out dispatcher queue is full");
    }

    /// <summary>Test hook: wait until the dispatcher worker finishes pending batches.</summary>
    public void Drain() => _dispatcher.WaitUntilIdle();

    /// <summary>Enqueues a single immediate send without draining the queue on the simulation thread.</summary>
    public void SendNow(Action send)
    {
        var batch = new WorldFanoutBatch();
        batch.AddOrdered("immediate", send);
        var seq = new WorldEventSequence(_tick, _phase, _sequence++);
        if (!_dispatcher.TryEnqueue(new SealedWorldFanoutBatch(batch, seq, 0)))
            throw new InvalidOperationException("fan-out dispatcher queue is full");
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
}