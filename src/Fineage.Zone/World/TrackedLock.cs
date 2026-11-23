using System.Diagnostics;

namespace Fineage.Zone.World;

/// <summary>
/// A monitor whose owner and waiters the step watchdog can name. Entering from a thread that does not already
/// hold it is recorded in <see cref="StepWaitRegistry"/> for the time spent blocked.
/// </summary>
internal sealed class TrackedLock
{
    readonly object _gate = new();
    readonly string _name;
    int _ownerThread;
    long _acquiredAt;
    int _depth;
    int _waiters;

    public TrackedLock(string name) => _name = name;

    public Hold Enter()
    {
        if (!Monitor.IsEntered(_gate))
        {
            Interlocked.Increment(ref _waiters);
            StepWaitRegistry.Push(_name);
            try
            {
                Monitor.Enter(_gate);
            }
            finally
            {
                Interlocked.Decrement(ref _waiters);
                StepWaitRegistry.Pop(_name);
            }

            _ownerThread = Environment.CurrentManagedThreadId;
            _acquiredAt = Stopwatch.GetTimestamp();
            _depth = 1;
        }
        else
        {
            Monitor.Enter(_gate);
            _depth++;
        }

        return new Hold(this);
    }

    public string Describe()
    {
        int owner = Volatile.Read(ref _ownerThread);
        int waiters = Volatile.Read(ref _waiters);
        if (owner == 0)
            return $"{_name}: free, waiters={waiters}";
        var held = Stopwatch.GetElapsedTime(Volatile.Read(ref _acquiredAt));
        return $"{_name}: held by thread {owner} for {held.TotalMilliseconds:F0} ms, waiters={waiters}";
    }

    void Exit()
    {
        if (--_depth == 0)
            Volatile.Write(ref _ownerThread, 0);
        Monitor.Exit(_gate);
    }

    internal readonly struct Hold : IDisposable
    {
        readonly TrackedLock _lock;

        public Hold(TrackedLock tracked) => _lock = tracked;

        public void Dispose() => _lock.Exit();
    }
}
