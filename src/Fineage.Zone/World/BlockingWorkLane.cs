using System.Collections.Concurrent;

namespace Fineage.Zone.World;

/// <summary>
/// Dedicated threads for synchronous persistence that a handler has to wait for (logout, eviction on re-enter).
/// The handler awaits the returned task, so the thread-pool thread is free while Postgres works. Before #1110 those
/// writes ran on the pool: a reconnect storm parked the pool's threads behind the store's single write gate, the
/// zone stopped answering Hello and the simulation loop could not resume.
/// </summary>
/// <remarks>
/// The store serialises writes behind one gate, so more threads would only queue on it; two let one logout wait for
/// its economy batch while the other writes snapshots. Work submitted before <see cref="Start"/> or after
/// <see cref="Stop"/> runs on the caller, which is the behaviour of a world without a hosted loop.
/// </remarks>
internal sealed class BlockingWorkLane
{
    readonly string _name;
    readonly int _threads;
    BlockingCollection<(Action Work, TaskCompletionSource Done)>? _queue;
    Thread[] _workers = [];

    public BlockingWorkLane(string name, int threads)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threads, 1);
        _name = name;
        _threads = threads;
    }

    public int Pending => _queue?.Count ?? 0;

    public void Start()
    {
        if (_queue != null)
            return;
        var queue = new BlockingCollection<(Action, TaskCompletionSource)>();
        _workers = Enumerable.Range(0, _threads).Select(i => new Thread(() => Drain(queue))
        {
            IsBackground = true,
            Name = $"{_name}-{i}",
        }).ToArray();
        _queue = queue;
        foreach (var worker in _workers)
            worker.Start();
    }

    /// <summary>Lets queued work finish, then stops the threads. Later work runs on its caller.</summary>
    public void Stop(TimeSpan join)
    {
        var queue = Interlocked.Exchange(ref _queue, null);
        if (queue == null)
            return;
        queue.CompleteAdding();
        foreach (var worker in _workers)
            worker.Join(join);
        _workers = [];
    }

    /// <summary>Runs <paramref name="work"/> on a lane thread; the task carries its exception.</summary>
    public Task RunAsync(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var queue = Volatile.Read(ref _queue);
        if (queue != null)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                queue.Add((work, done));
                return done.Task;
            }
            catch (InvalidOperationException)
            {
                // Stopped between the read and the add: run it here like any other late work.
            }
        }

        try
        {
            work();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    static void Drain(BlockingCollection<(Action Work, TaskCompletionSource Done)> queue)
    {
        foreach (var (work, done) in queue.GetConsumingEnumerable())
        {
            try
            {
                work();
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }
    }
}
