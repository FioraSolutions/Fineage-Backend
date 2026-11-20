namespace Fineage.Zone.World;

/// <summary>
/// Player commands on their way into the simulation. Handlers submit from I/O threads and await the reply; the
/// <c>commands</c> stage takes everything submitted so far at the start of a step, in submission order. The step that
/// applied them takes their replies when it seals its fan-out (<see cref="TakeApplied"/>) and completes them after its
/// inventory writes and fan-out drain: a handler's answer never overtakes what its command caused.
/// </summary>
internal sealed class ZoneCommandIngress
{
    readonly object _lock = new();
    readonly SemaphoreSlim _submitted = new(0);
    List<Submission> _queued = [];
    List<Applied> _applied = [];
    long _sequence;
    string? _closed;

    internal sealed record Submission(long Sequence, ZoneSession Session, ZoneCommand Command, TaskCompletionSource<string?> Reply);

    internal readonly record struct Applied(Submission Submission, string? Error, Exception? Failure);

    public Task<string?> Submit(ZoneSession session, ZoneCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(command);
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (_closed != null)
                return Task.FromException<string?>(new InvalidOperationException(
                    $"command {command.Name} for object {session.ObjectId} refused: {_closed}"));
            _queued.Add(new Submission(++_sequence, session, command, reply));
        }

        if (ct.CanBeCanceled)
        {
            // A connection token lives as long as the connection: drop the registration once the reply is settled.
            var registration = ct.Register(static state => ((TaskCompletionSource<string?>)state!).TrySetCanceled(), reply);
            reply.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state!).Dispose(), registration,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        _submitted.Release();
        return reply.Task;
    }

    /// <summary>Everything submitted so far, in submission order; later submissions wait for the next drain.</summary>
    public IReadOnlyList<Submission> TakeQueued()
    {
        lock (_lock)
        {
            var taken = _queued;
            _queued = [];
            return taken;
        }
    }

    /// <summary>Under the gate: the outcome of an applied command; its reply waits for <see cref="TakeApplied"/>.</summary>
    public void Record(Submission submission, string? error, Exception? failure)
    {
        lock (_lock)
            _applied.Add(new Applied(submission, error, failure));
    }

    /// <summary>
    /// Under the gate, right after the step's fan-out batch was sealed: the replies of the commands it applied. The
    /// caller completes them with <see cref="Complete"/> once its own inventory writes and fan-out drain are done.
    /// </summary>
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

    /// <summary>Waits until something was submitted since the last wait (hand-driven hosts pump with it).</summary>
    public Task WaitForSubmissionAsync(CancellationToken ct) => _submitted.WaitAsync(ct);

    /// <summary>
    /// Refuses further submissions and fails the ones still queued with <paramref name="reason"/>, so no handler
    /// waits on a simulation that stopped.
    /// </summary>
    public void Close(string reason)
    {
        List<Submission> stranded;
        lock (_lock)
        {
            _closed = reason;
            stranded = _queued;
            _queued = [];
        }

        foreach (var submission in stranded)
            submission.Reply.TrySetException(new InvalidOperationException(
                $"command {submission.Command.Name} for object {submission.Session.ObjectId} was not applied: {reason}"));
    }
}
