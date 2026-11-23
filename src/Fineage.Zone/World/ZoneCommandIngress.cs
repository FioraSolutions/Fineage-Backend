using System.Diagnostics;
using Fineage.Protocol.Telemetry;

namespace Fineage.Zone.World;

/// <summary>Submit timestamp carried on the sealed fan-out batch so mailbox delivery can record submit→mailbox.</summary>
internal readonly record struct CommandSubmitMark(long CorrelationId, long SubmittedAtTicks);

/// <summary>
/// One ordered queue per player and a deficit round-robin over ready players. The quantum is command cost
/// units, so a flooding player cannot take the tick away from everyone else, and commands from one player
/// stay in submission order. Consecutive pending movement commands collapse to the latest; a non-movement
/// command between them is a boundary. The apply budget is wall-clock
/// (<see cref="ZoneSimulationSettings.CommandApplyBudgetFraction"/> of the step, plus banked unused time while
/// a backlog remains) clamped to the tick deadline. <c>FINEAGE_ZONE_COMMAND_APPLY_BUDGET</c> is an optional
/// extra cost-unit cap, not the default scheduler.
/// Region-parallel apply is out of scope: every player queue is still drained on the simulation thread.
/// A later shard can only split ready players after this scheduler remains the single admission point,
/// so one player is never applied by two threads.
/// </summary>
internal sealed class ZoneCommandIngress
{
    internal const string CoalescedReason = "coalesced: superseded by a later movement command";

    readonly object _lock = new();
    readonly SemaphoreSlim _submitted = new(0, int.MaxValue);
    readonly Dictionary<int, PlayerQueue> _players = new();
    readonly List<int> _active = [];
    readonly CommandApplyTimebox _timebox;
    readonly int _queueDepthLimit;
    readonly int _maxAgeMs;
    readonly int _quantum;
    readonly int _costCap;
    readonly double _stepMs;
    int _cursor;
    bool _visitCharged;
    bool _cycleStarved;
    long _sequence;
    string? _closed;
    int _depth;
    int _coalescedThisTick;
    long _sliceStartTicks;
    long _clockTicks;
    bool _clockSet;

    internal ZoneCommandIngress(IngressLimits? limits = null)
    {
        limits ??= IngressLimits.FromSettings();
        _queueDepthLimit = limits.QueueDepthLimit;
        _maxAgeMs = limits.MaxAgeMs;
        _quantum = limits.Quantum;
        _costCap = limits.CostCap;
        _stepMs = limits.StepMs;
        _timebox = new CommandApplyTimebox(limits.NominalBudgetMs, limits.StepMs);
    }

    /// <summary>Test clock. Unset uses <see cref="Stopwatch.GetTimestamp"/>.</summary>
    internal long? ClockForTest
    {
        get
        {
            lock (_lock)
                return _clockSet ? _clockTicks : null;
        }
        set
        {
            lock (_lock)
            {
                if (value is { } ticks)
                {
                    _clockTicks = ticks;
                    _clockSet = true;
                }
                else
                {
                    _clockSet = false;
                }
            }
        }
    }

    internal sealed record IngressLimits(
        int QueueDepthLimit,
        int MaxAgeMs,
        int Quantum,
        int CostCap,
        double NominalBudgetMs,
        double StepMs)
    {
        public static IngressLimits FromSettings()
        {
            int? cost = ZoneSimulationSettings.CommandApplyCostCapOverride;
            return new IngressLimits(
                ZoneSimulationSettings.CommandQueueDepthLimit,
                ZoneSimulationSettings.CommandQueueMaxAgeMs,
                ZoneSimulationSettings.CommandDrrQuantum,
                cost is null or 0 ? int.MaxValue : cost.Value,
                ZoneSimulationSettings.CommandApplyNominalMilliseconds,
                ZoneSimulationSettings.StepMilliseconds);
        }
    }

    internal readonly record struct ApplySlice(long DeadlineTimestamp, int CostRemaining);

    internal sealed record Submission(
        long CorrelationId,
        ZoneSession Session,
        ZoneCommand Command,
        TaskCompletionSource<string?> Reply,
        long SubmittedAtTicks);

    internal readonly record struct Applied(Submission Submission, string? Error, Exception? Failure);

    sealed class PlayerQueue
    {
        public readonly List<Submission> Items = [];
        public int Deficit;
        public long OldestTicks;
        public int Count => Items.Count;
        public Submission Head => Items[0];
        public Submission Tail => Items[^1];

        public void Enqueue(Submission submission)
        {
            if (Items.Count == 0)
                OldestTicks = submission.SubmittedAtTicks;
            Items.Add(submission);
        }

        public Submission DequeueHead()
        {
            var head = Items[0];
            Items.RemoveAt(0);
            if (Items.Count > 0)
                OldestTicks = Items[0].SubmittedAtTicks;
            return head;
        }

        public Submission ReplaceTail(Submission newer)
        {
            var old = Items[^1];
            Items[^1] = newer;
            if (Items.Count == 1)
                OldestTicks = newer.SubmittedAtTicks;
            return old;
        }
    }

    public Task<string?> Submit(ZoneSession session, ZoneCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(command);
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Submission? coalesced = null;
        string? refusal;
        lock (_lock)
        {
            if (_closed != null)
            {
                refusal = $"command {command.Name} for object {session.ObjectId} refused: {_closed}";
                ZoneLog.Warn(refusal);
                return Task.FromException<string?>(new ZoneIngressRefusal(refusal));
            }

            var submission = new Submission(++_sequence, session, command, reply, NowTicks());
            if (!EnqueueLocked(submission, out coalesced, out refusal))
            {
                ZoneLog.Warn(refusal!);
                FineageTelemetry.CommandRejected.Add(1, new KeyValuePair<string, object?>("reason", ReasonTag(refusal!)));
                return Task.FromException<string?>(new ZoneIngressRefusal(refusal!));
            }

            if (_submitted.CurrentCount == 0)
                _submitted.Release();
        }

        coalesced?.Reply.TrySetResult(CoalescedReason);

        if (ct.CanBeCanceled)
        {
            var registration = ct.Register(static state => ((TaskCompletionSource<string?>)state!).TrySetCanceled(), reply);
            reply.Task.ContinueWith(static (_, state) => ((CancellationTokenRegistration)state!).Dispose(), registration,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        return reply.Task;
    }

    bool EnqueueLocked(Submission submission, out Submission? coalesced, out string? refusal)
    {
        coalesced = null;
        refusal = null;
        int sessionId = submission.Session.ObjectId;
        if (!_players.TryGetValue(sessionId, out var queue))
        {
            queue = new PlayerQueue();
            _players.Add(sessionId, queue);
        }

        if (queue.Count > 0)
        {
            double ageMs = AgeMs(queue.OldestTicks, submission.SubmittedAtTicks);
            if (ageMs > _maxAgeMs && !IsConsecutiveMovement(queue.Tail.Command, submission.Command))
            {
                refusal = $"command {submission.Command.Name} for object {sessionId} refused: " +
                          $"rejected: queue age {ageMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}ms exceeds {_maxAgeMs}ms";
                return false;
            }
        }

        if (queue.Count > 0 && IsConsecutiveMovement(queue.Tail.Command, submission.Command))
        {
            coalesced = queue.ReplaceTail(submission);
            _coalescedThisTick++;
            FineageTelemetry.CommandCoalesced.Add(1);
            SamplePlayerLocked(queue);
            return true;
        }

        if (queue.Count >= _queueDepthLimit)
        {
            refusal = $"command {submission.Command.Name} for object {sessionId} refused: " +
                      $"rejected: queue depth {queue.Count} is at the limit {_queueDepthLimit}";
            return false;
        }

        bool wasEmpty = queue.Count == 0;
        queue.Enqueue(submission);
        _depth++;
        if (wasEmpty)
            _active.Add(sessionId);
        SamplePlayerLocked(queue);
        return true;
    }

    static bool IsConsecutiveMovement(ZoneCommand older, ZoneCommand newer) =>
        older.IsCoalesceableMovement && newer.IsCoalesceableMovement;

    static string ReasonTag(string refusal) =>
        refusal.Contains("queue age", StringComparison.Ordinal) ? "queue_age"
        : refusal.Contains("queue depth", StringComparison.Ordinal) ? "queue_depth"
        : "closed";

    /// <summary>Opens the wall-clock slice for this step. <paramref name="stepStartedTimestamp"/> is 0 outside the live loop.</summary>
    internal ApplySlice BeginApply(long stepStartedTimestamp)
    {
        lock (_lock)
        {
            _cycleStarved = false;
            SampleAllLocked();
            bool backlog = _depth > 0;
            double grantMs = _timebox.GrantMilliseconds(backlog);
            long now = NowTicks();
            _sliceStartTicks = now;
            long budgetEnd = now + MillisecondsToTicks(grantMs);
            long tickEnd = stepStartedTimestamp > 0
                ? stepStartedTimestamp + MillisecondsToTicks(_stepMs)
                : budgetEnd;
            if (stepStartedTimestamp > 0 && now >= tickEnd)
            {
                double ageMs = TicksToMs(now - stepStartedTimestamp);
                ZoneLog.Error(
                    $"command ingress tick deadline already passed at step start (step age {ageMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}ms, step {_stepMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}ms); " +
                    $"applying the nominal {_timebox.NominalMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}ms budget so the per-player queues cannot stall");
                tickEnd = now + MillisecondsToTicks(_timebox.NominalMs);
            }

            long deadline = Math.Min(budgetEnd, tickEnd);
            return new ApplySlice(deadline, _costCap);
        }
    }

    /// <summary>Deficit round-robin: one command, or false when the slice or the ready set is exhausted.</summary>
    internal bool TryTake(ApplySlice slice, ref int costRemaining, out Submission submission)
    {
        lock (_lock)
        {
            submission = null!;
            if (_cycleStarved || NowTicks() >= slice.DeadlineTimestamp)
                return false;

            // One quantum per player per call. A second pass would credit a player who already could not send.
            int advances = 0;
            int advanceLimit = Math.Max(1, _active.Count);
            while (_active.Count > 0 && advances < advanceLimit)
            {
                if (_cursor >= _active.Count)
                    _cursor = 0;
                int sessionId = _active[_cursor];
                if (!_players.TryGetValue(sessionId, out var queue) || queue.Count == 0)
                {
                    RemoveActiveAtCursor();
                    continue;
                }

                if (!_visitCharged)
                {
                    queue.Deficit += _quantum;
                    _visitCharged = true;
                }

                int cost = Math.Max(1, queue.Head.Command.ApplyCost);
                if (cost > queue.Deficit || cost > costRemaining)
                {
                    AdvanceCursor();
                    advances++;
                    continue;
                }

                submission = queue.DequeueHead();
                queue.Deficit -= cost;
                costRemaining -= cost;
                _depth--;
                double waitMs = AgeMs(submission.SubmittedAtTicks, NowTicks());
                FineageTelemetry.CommandQueueWaitMilliseconds.Record(waitMs,
                    new KeyValuePair<string, object?>("class", submission.Command.IngressClass.ToString().ToLowerInvariant()));
                FineageTelemetry.CommandTimelineMilliseconds.Record(waitMs,
                    new KeyValuePair<string, object?>("phase", "submit_to_dequeue"));

                if (queue.Count == 0)
                {
                    queue.Deficit = 0;
                    _players.Remove(sessionId);
                    RemoveActiveAtCursor();
                }
                else if (Math.Max(1, queue.Head.Command.ApplyCost) > queue.Deficit)
                {
                    AdvanceCursor();
                }

                return true;
            }

            _cycleStarved = _active.Count > 0;
            return false;
        }
    }

    internal void FinishApply()
    {
        int coalesced;
        lock (_lock)
        {
            double usedMs = TicksToMs(NowTicks() - _sliceStartTicks);
            _timebox.Account(usedMs);
            coalesced = _coalescedThisTick;
            _coalescedThisTick = 0;
            if (_depth > 0 && _submitted.CurrentCount == 0)
                _submitted.Release();
        }

        if (coalesced > 0)
            ZoneLog.Info($"command ingress coalesced {coalesced} movement command(s): {CoalescedReason}");
    }

    void AdvanceCursor()
    {
        _cursor++;
        _visitCharged = false;
    }

    void RemoveActiveAtCursor()
    {
        _active.RemoveAt(_cursor);
        _visitCharged = false;
        if (_cursor >= _active.Count)
            _cursor = 0;
    }

    void SampleAllLocked()
    {
        double maxAge = 0;
        foreach (var queue in _players.Values)
        {
            if (queue.Count == 0)
                continue;
            double age = AgeMs(queue.OldestTicks, NowTicks());
            if (age > maxAge)
                maxAge = age;
            SamplePlayerLocked(queue, age);
        }

        FineageTelemetry.NoteCommandQueueAge(maxAge);
    }

    void SamplePlayerLocked(PlayerQueue queue, double? ageMs = null)
    {
        FineageTelemetry.CommandQueueDepth.Record(queue.Count);
        double age = ageMs ?? (queue.Count == 0 ? 0 : AgeMs(queue.OldestTicks, NowTicks()));
        if (queue.Count > 0)
            FineageTelemetry.CommandQueueAgeMilliseconds.Record(age);
    }

    List<Applied> _applied = [];

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

    public void FailUncommitted(IReadOnlySet<int> characterIds, string error)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        ArgumentException.ThrowIfNullOrEmpty(error);
        lock (_lock)
        {
            for (int i = 0; i < _applied.Count; i++)
            {
                var row = _applied[i];
                if (row.Error != null || row.Failure != null)
                    continue;
                if (!characterIds.Contains(row.Submission.Session.ObjectId))
                    continue;
                _applied[i] = row with { Error = error };
            }
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
                return _depth;
        }
    }

    internal double BankedApplyMsForTest
    {
        get
        {
            lock (_lock)
                return _timebox.CreditMs;
        }
    }

    public void Close(string reason)
    {
        List<Submission> stranded;
        lock (_lock)
        {
            _closed = reason;
            stranded = [];
            foreach (var queue in _players.Values)
            {
                while (queue.Count > 0)
                    stranded.Add(queue.DequeueHead());
            }

            _players.Clear();
            _active.Clear();
            _depth = 0;
        }

        foreach (var submission in stranded)
            submission.Reply.TrySetException(new ZoneIngressRefusal(
                $"command {submission.Command.Name} for object {submission.Session.ObjectId} was not applied: {reason}"));
    }

    long NowTicks()
    {
        if (_clockSet)
            return _clockTicks;
        return Stopwatch.GetTimestamp();
    }

    double AgeMs(long submittedAtTicks, long nowTicks) =>
        Math.Max(0, TicksToMs(nowTicks - submittedAtTicks));

    static long MillisecondsToTicks(double ms) =>
        (long)(ms * Stopwatch.Frequency / 1000.0);

    static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}

/// <summary>Ingress refused the command before it was applied. <see cref="Coalesced"/> is a superseded movement.</summary>
internal sealed class ZoneIngressRefusal : InvalidOperationException
{
    public ZoneIngressRefusal(string reason) : base(reason)
    {
        Reason = reason;
    }

    public string Reason { get; }

    public bool Coalesced => Reason.StartsWith("coalesced:", StringComparison.Ordinal);
}
