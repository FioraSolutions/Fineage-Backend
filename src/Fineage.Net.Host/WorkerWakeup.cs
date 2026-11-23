namespace Fineage.Net.Host;

/// <summary>
/// Wakes one dedicated delivery thread without touching the thread pool or taking a managed lock on the producer.
/// <para>
/// The producer publishes its work first (any interlocked queue write) and then calls <see cref="Signal"/>, which sets
/// the event only when the worker announced it is parking. The worker announces with a full fence and re-checks for
/// work before it sleeps, so a signal cannot be lost between the check and the wait. Async waits on the pool were the
/// fan-out latency: under load the worker's continuation queued behind socket pumps for tens to hundreds of ms.
/// </para>
/// </summary>
public sealed class WorkerWakeup : IDisposable
{
    readonly AutoResetEvent _event = new(false);
    int _parked;
    int _stopped;

    public bool Stopped => Volatile.Read(ref _stopped) != 0;

    /// <summary>Producer side. Never blocks: at most one kernel event set when the worker is parked.</summary>
    public void Signal()
    {
        Interlocked.MemoryBarrier();
        if (Volatile.Read(ref _parked) != 0)
            _event.Set();
    }

    /// <summary>Worker side: sleeps until <see cref="Signal"/> or <see cref="Stop"/> unless <paramref name="hasWork"/> is already true.</summary>
    public void Park(Func<bool> hasWork)
    {
        Interlocked.Exchange(ref _parked, 1);
        try
        {
            if (hasWork() || Stopped)
                return;
            _event.WaitOne();
        }
        finally
        {
            Volatile.Write(ref _parked, 0);
        }
    }

    public void Stop()
    {
        Volatile.Write(ref _stopped, 1);
        _event.Set();
    }

    public void Dispose() => _event.Dispose();
}
