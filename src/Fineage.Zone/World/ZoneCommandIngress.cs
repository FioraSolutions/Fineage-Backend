using System.Diagnostics;
using System.Runtime.InteropServices;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>
/// Player commands on their way into the simulation. Three ingress classes (interactive, gameplay, lifecycle) with
/// fair round-robin across sessions, strict FIFO within a session, and a per-step apply budget measured in
/// deterministic command cost units. Interactive ingress coalesces unapplied <see cref="MoveCommand"/> per session.
/// </summary>
internal sealed class ZoneCommandIngress
{
    const int DefaultQueueCapacity = 512;

    readonly object _lock = new();
    readonly SemaphoreSlim _submitted = new(0, int.MaxValue);
    readonly Dictionary<int, SessionQueues> _sessions = new();
    readonly List<int> _roundRobin = [];
    int _roundRobinCursor;
    List<Applied> _applied = [];
    long _sequence;
    string? _closed;
    int _lifecycleDepth;

    internal sealed record Submission(long Sequence, ZoneSession Session, ZoneCommand Command, TaskCompletionSource<string?> Reply, long SubmittedAtTicks);

    internal readonly record struct Applied(Submission Submission, string? Error, Exception? Failure);

    sealed class SessionQueues
    {
        public readonly Queue<Submission> Interactive = new();
        public readonly Queue<Submission> Gameplay = new();
        public readonly Queue<Submission> Lifecycle = new();
        public Submission? PendingMove;
        public long PendingMoveSequence;
        public int Depth;
    }

    public Task<string?> Submit(ZoneSession session, ZoneCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(command);
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        long now = Stopwatch.GetTimestamp();
        lock (_lock)
        {
            if (_closed != null)
                return Task.FromException<string?>(new InvalidOperationException(
                    $"command {command.Name} for object {session.ObjectId} refused: {_closed}"));

            var submission = new Submission(++_sequence, session, command, reply, now);
            if (!EnqueueLocked(session.ObjectId, submission))
                return Task.FromException<string?>(new InvalidOperationException(
                    $"command {command.Name} for object {session.ObjectId} refused: ingress overloaded for {command.IngressClass}"));

            if (_submitted.CurrentCount == 0)
                _submitted.Release();
        }

        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(static state => ((TaskCompletionSource<string?>)state!).TrySetCanceled(), reply);
            reply.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state!).Dispose(), registration,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        return reply.Task;
    }

    bool EnqueueLocked(int sessionId, Submission submission)
    {
        ref var queues = ref CollectionsMarshal.GetValueRefOrAddDefault(_sessions, sessionId, out bool exists);
        if (!exists)
        {
            queues = new SessionQueues();
            _roundRobin.Add(sessionId);
        }

        queues!.Depth++;
        RecordQueueDepth(submission.Command.IngressClass, queues.Depth);
        FineageTelemetry.CommandQueueWaitMilliseconds.Record(WaitMs(submission.SubmittedAtTicks),
            new KeyValuePair<string, object?>("class", submission.Command.IngressClass.ToString().ToLowerInvariant()));

        switch (submission.Command.IngressClass)
        {
            case ZoneCommandClass.Interactive when submission.Command is MoveCommand move:
                queues.PendingMove = submission with { Command = move };
                queues.PendingMoveSequence = submission.Sequence;
                return true;
            case ZoneCommandClass.Interactive:
                if (AtCapacity(queues.Interactive))
                    return RefuseEnqueue(queues);
                queues.Interactive.Enqueue(submission);
                return true;
            case ZoneCommandClass.Lifecycle:
                if (AtCapacity(queues.Lifecycle))
                    return RefuseEnqueue(queues);
                queues.Lifecycle.Enqueue(submission);
                _lifecycleDepth++;
                return true;
            case ZoneCommandClass.Gameplay:
                if (AtCapacity(queues.Gameplay))
                    return RefuseEnqueue(queues);
                queues.Gameplay.Enqueue(submission);
                return true;
            default:
                throw new InvalidOperationException($"command {submission.Command.GetType().Name} has no ingress class");
        }
    }

    static bool AtCapacity(Queue<Submission> queue) => queue.Count >= DefaultQueueCapacity;

    bool RefuseEnqueue(SessionQueues queues)
    {
        queues.Depth--;
        return false;
    }

    /// <summary>Takes the next commands for the step up to <paramref name="budgetCost"/> units.</summary>
    public IReadOnlyList<Submission> TakeQueued(int budgetCost)
    {
        lock (_lock)
        {
            if (_sessions.Count == 0)
                return [];

            var taken = new List<Submission>();
            int remaining = budgetCost <= 0 ? int.MaxValue : budgetCost;
            int guard = 0;
            while (remaining > 0 && _roundRobin.Count > 0 && guard++ < 100_000)
            {
                if (_roundRobinCursor >= _roundRobin.Count)
                    _roundRobinCursor = 0;
                int sessionId = _roundRobin[_roundRobinCursor];
                if (!_sessions.TryGetValue(sessionId, out var queues) || queues.Depth == 0)
                {
                    _roundRobin.RemoveAt(_roundRobinCursor);
                    continue;
                }

                Submission? next = DequeueOneLocked(queues, remaining, out int cost);
                if (next == null)
                {
                    _roundRobinCursor++;
                    continue;
                }

                taken.Add(next);
                remaining -= cost;
                queues.Depth--;
                _roundRobinCursor++;
                if (queues.Depth == 0)
                {
                    _sessions.Remove(sessionId);
                    _roundRobin.Remove(sessionId);
                    if (_roundRobinCursor > 0)
                        _roundRobinCursor--;
                }
            }

            return taken;
        }
    }

    Submission? DequeueOneLocked(SessionQueues queues, int remainingBudget, out int cost)
    {
        if (queues.PendingMove is { } move)
        {
            cost = move.Command.ApplyCost;
            if (cost > remainingBudget && remainingBudget != int.MaxValue)
            {
                cost = 0;
                return null;
            }

            queues.PendingMove = null;
            return move;
        }

        if (queues.Interactive.Count > 0)
        {
            var s = queues.Interactive.Dequeue();
            cost = s.Command.ApplyCost;
            if (cost > remainingBudget && remainingBudget != int.MaxValue)
            {
                queues.Interactive.Enqueue(s);
                cost = 0;
                return null;
            }

            return s;
        }

        if (queues.Lifecycle.Count > 0)
        {
            var s = queues.Lifecycle.Dequeue();
            cost = s.Command.ApplyCost;
            if (cost > remainingBudget && remainingBudget != int.MaxValue)
            {
                queues.Lifecycle.Enqueue(s);
                cost = 0;
                return null;
            }

            _lifecycleDepth--;
            return s;
        }

        if (queues.Gameplay.Count > 0)
        {
            var s = queues.Gameplay.Dequeue();
            cost = s.Command.ApplyCost;
            if (cost > remainingBudget && remainingBudget != int.MaxValue)
            {
                queues.Gameplay.Enqueue(s);
                cost = 0;
                return null;
            }

            return s;
        }

        cost = 0;
        return null;
    }

    public void Record(Submission submission, string? error, Exception? failure)
    {
        lock (_lock)
            _applied.Add(new Applied(submission, error, failure));
    }

    public IReadOnlyList<Applied> TakeApplied()
    {
        lock (_lock)
        {
            if (_applied.Count == 0)
                return [];
            var taken = _applied;
            _applied = [];
            return taken;
        }
    }

    public static void Complete(IReadOnlyList<Applied> applied)
    {
        foreach (var (submission, error, failure) in applied)
        {
            if (failure != null)
                submission.Reply.TrySetException(failure);
            else
                submission.Reply.TrySetResult(error);
        }
    }

    public Task WaitForSubmissionAsync(CancellationToken ct) => _submitted.WaitAsync(ct);

    internal int WakeSignalCountForTest => _submitted.CurrentCount;

    internal int QueuedDepth
    {
        get
        {
            lock (_lock)
                return _sessions.Values.Sum(q => q.Depth);
        }
    }

    internal int LifecycleDepth
    {
        get
        {
            lock (_lock)
                return _lifecycleDepth;
        }
    }

    public void Close(string reason)
    {
        List<Submission> stranded;
        lock (_lock)
        {
            _closed = reason;
            stranded = [];
            foreach (var queues in _sessions.Values)
            {
                if (queues.PendingMove is { } move)
                    stranded.Add(move);
                while (queues.Interactive.TryDequeue(out var s))
                    stranded.Add(s);
                while (queues.Gameplay.TryDequeue(out var s))
                    stranded.Add(s);
                while (queues.Lifecycle.TryDequeue(out var s))
                    stranded.Add(s);
            }

            _sessions.Clear();
            _roundRobin.Clear();
        }

        foreach (var submission in stranded)
            submission.Reply.TrySetException(new InvalidOperationException(
                $"command {submission.Command.Name} for object {submission.Session.ObjectId} was not applied: {reason}"));
    }

    static double WaitMs(long submittedAtTicks) =>
        Math.Max(0, (Stopwatch.GetTimestamp() - submittedAtTicks) * 1000.0 / Stopwatch.Frequency);

    static void RecordQueueDepth(ZoneCommandClass cls, int depth) =>
        FineageTelemetry.CommandQueueDepth.Record(depth,
            new KeyValuePair<string, object?>("class", cls.ToString().ToLowerInvariant()));
}