using System.Diagnostics;
using System.Globalization;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Command ingress, the per-tick command log and its replay. See sections 3.3 and 4 of
/// <c>docs/architecture/zone-simulation.md</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    const string CommandsStage = "commands";
    const string PhaseDequeue = "commands.ingress_dequeue";
    const string PhaseApply = "commands.apply";
    const string PhaseFlush = "commands.flush_dirty_inventory";
    const string PhaseSeal = "commands.fanout_seal";
    const string PhaseReply = "commands.reply_completion";

    /// <summary>Client-visible failure when the ledger commit did not land. Success is not reported in that case.</summary>
    internal const string EconomyCommitFailed = "economy_commit_failed: auditable economy commit failed";

    readonly ZoneCommandIngress _ingress = new();

    internal ZoneCommandIngress CommandIngress => _ingress;
    SimulationLog? _commandLog;

    internal int SubmissionWakeCountForTest => _ingress.WakeSignalCountForTest;
    internal int CommandIngressQueuedDepth => _ingress.QueuedDepth;

    /// <summary>
    /// Submits <paramref name="command"/> for <paramref name="session"/>. The live loop applies it through this
    /// ingress as soon as the thread is between ticks, at the upcoming tick's time; a step's phase 0 applies whatever
    /// is still queued. The task completes with its rejection reason (or null) once that admission's fan-out was sent.
    /// </summary>
    public Task<string?> ExecuteAsync(ZoneSession session, ZoneCommand command, CancellationToken ct) =>
        _ingress.Submit(session, command, ct);

    /// <summary>
    /// One admission slice while the loop is waiting for the next tick. Same ingress, same <c>commands</c> stage,
    /// pinned to tick <c>Tick + 1</c> so the apply time matches the phase 0 that follows. The slice stops at that
    /// tick boundary. Fan-out for the slice is sealed on its own phase so it does not reuse the step's sequence.
    /// </summary>
    void AdmitQueuedCommands(long untilMs)
    {
        long now = Stopwatch.GetTimestamp();
        long stepTicks = (long)(ZoneSimulationSettings.StepMilliseconds * Stopwatch.Frequency / 1000.0);
        long untilTicks = (long)(untilMs * Stopwatch.Frequency / 1000.0);
        long syntheticStart = now - Math.Max(0, stepTicks - untilTicks);
        long wall = _simulationClock.WallUnixMs();
        Volatile.Write(ref _stepStartedTimestamp, syntheticStart);
        try
        {
            RunStep(_ =>
            {
                _fanout.ConfigureSequence(_simulationTicks.Tick + 1, phase: 1);
                _simulationTicks.RunAhead(CommandsStage, wall);
            });
        }
        catch (Exception ex)
        {
            FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", "command-admission"));
            ZoneHotPathLogs.SimulationStepFailedOutsideStages(_simulationTicks.Tick, ex);
        }
        finally
        {
            Volatile.Write(ref _stepStartedTimestamp, 0L);
        }
    }

    /// <summary>
    /// Waits until the next tick or until a command is queued, whichever comes first.
    /// Returns true when the tick is due. Returns false when a command should be admitted and the loop should
    /// look again. Cancelling <paramref name="ct"/> throws.
    /// </summary>
    /// <remarks>
    /// A blocking wait on the loop's own thread: neither the tick nor the wake-up needs a thread-pool thread, so a pool
    /// starved by blocked handlers cannot hold the next step back (#1110).
    /// </remarks>
    bool WaitForTickOrCommand(long waitMs, CancellationToken ct)
    {
        bool submitted = _ingress.WaitForSubmission((int)Math.Min(waitMs, int.MaxValue), ct);
        return !submitted;
    }

    /// <summary>
    /// Phase 0: deficit round-robin over per-player queues, inside the wall-clock apply slice. Ordering is per
    /// player; players share the slice. A command cancelled before it is dequeued never reaches this loop.
    /// </summary>
    void ApplyCommands(TickContext tick)
    {
        using var commandScope = _syncIo.CommandScope();
        bool applied = false;
        var slice = _ingress.BeginApply(Volatile.Read(ref _stepStartedTimestamp));
        int costRemaining = slice.CostRemaining;
        long dequeueTicks = 0;
        long applyTicks = 0;
        var applyMsByCommand = new Dictionary<string, double>(StringComparer.Ordinal);
        var submits = new List<CommandSubmitMark>();
        try
        {
            while (true)
            {
                long dequeueStart = Stopwatch.GetTimestamp();
                bool took = _ingress.TryTake(slice, ref costRemaining, out var submission);
                dequeueTicks += Stopwatch.GetTimestamp() - dequeueStart;
                if (!took)
                    break;

                // Cancelled by its connection before this tick: it never happened.
                if (submission.Reply.Task.IsCompleted)
                    continue;

                string? error = null;
                Exception? failure = null;
                long oneStart = Stopwatch.GetTimestamp();
                using var commandActivity = FineageTelemetry.Activity.StartActivity("zone.command");
                commandActivity?.SetTag("command", submission.Command.Name);
                commandActivity?.SetTag("fineage.correlation_id", submission.CorrelationId);
                try
                {
                    error = submission.Command.Apply(this, submission.Session, tick);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    FineageTelemetry.StageFailures.Add(1, new KeyValuePair<string, object?>("stage", CommandsStage));
                    ZoneLog.Warn($"command {submission.Command} for object {submission.Session.ObjectId} failed at tick {tick.Tick}: {ex}");
                }

                applyTicks += Stopwatch.GetTimestamp() - oneStart;
                double oneMs = Stopwatch.GetElapsedTime(oneStart).TotalMilliseconds;
                string name = submission.Command.Name;
                applyMsByCommand[name] = applyMsByCommand.GetValueOrDefault(name) + oneMs;
                FineageTelemetry.CommandApplyMilliseconds.Record(oneMs,
                    new KeyValuePair<string, object?>("command", name));
                FineageTelemetry.CommandTimelineMilliseconds.Record(oneMs,
                    new KeyValuePair<string, object?>("phase", "dequeue_to_apply"));

                if (submission.Command.Logged)
                    _commandLog?.AppendCommand(tick.Tick, tick.WallUnixMs, submission.Session.ObjectId, submission.Command,
                        failure != null ? $"threw {failure.GetType().Name}" : error ?? "ok");
                _ingress.Record(submission, error, failure);
                submits.Add(new CommandSubmitMark(submission.CorrelationId, submission.SubmittedAtTicks));
                applied = true;
            }
        }
        finally
        {
            _ingress.FinishApply();
        }

        RecordCommandPhaseTicks(PhaseDequeue, dequeueTicks);
        double applyMs = RecordCommandPhaseTicks(PhaseApply, applyTicks);
        double flushMs = 0, sealMs = 0, replyMs = 0;
        // Replies carried from an earlier step still need a commit when no new command arrived (#1112).
        if (applied || _economyCarried.Count > 0)
            (flushMs, sealMs, replyMs) = ReleaseAppliedCommandReplies(submits);

        string? syncIo = _syncIo.ConsumeSummary();
        if (syncIo != null)
            ZoneLog.Info($"commands tick={tick.Tick} sync-io on simulation thread: {syncIo}");

        double totalMs = applyMs + flushMs + sealMs + replyMs;
        if (totalMs >= 100 && (applyMsByCommand.Count > 0 || flushMs + sealMs + replyMs > 0))
        {
            string breakdown = string.Join(", ",
                applyMsByCommand.OrderByDescending(pair => pair.Value).Take(8)
                    .Select(pair => $"{pair.Key}={pair.Value.ToString("F1", CultureInfo.InvariantCulture)}ms"));
            ZoneLog.Warn(
                $"commands stage tick={tick.Tick} total={totalMs.ToString("F1", CultureInfo.InvariantCulture)}ms " +
                $"apply={applyMs.ToString("F1", CultureInfo.InvariantCulture)}ms " +
                $"flush_dirty_inventory={flushMs.ToString("F1", CultureInfo.InvariantCulture)}ms " +
                $"fanout_seal={sealMs.ToString("F1", CultureInfo.InvariantCulture)}ms " +
                $"reply_completion={replyMs.ToString("F1", CultureInfo.InvariantCulture)}ms: {breakdown}");
        }
    }

    static double RecordCommandPhase(string phase, long started) =>
        RecordCommandPhaseTicks(phase, Stopwatch.GetTimestamp() - started);

    static double RecordCommandPhaseTicks(string phase, long ticks)
    {
        double ms = ticks * 1000.0 / Stopwatch.Frequency;
        if (ms < 0)
            ms = 0;
        FineageTelemetry.StageMilliseconds.Record(ms, new KeyValuePair<string, object?>("stage", phase));
        return ms;
    }

    /// <summary>
    /// Applies commands as they are submitted, without advancing the clock: hosts whose clock is driven by hand
    /// (tests, tools) have no simulation loop to drain them.
    /// </summary>
    internal async Task PumpCommandsAsync(CancellationToken ct)
    {
        _affinity.Drive();
        try
        {
            while (true)
            {
                await _ingress.WaitForSubmissionAsync(ct).ConfigureAwait(false);
                RunStep(wall => _simulationTicks.RunNow(CommandsStage, wall));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Starts recording every step and command of this world into a fresh log (tests and diagnostics).</summary>
    internal SimulationLog RecordSimulation()
    {
        using (_affinity.Step())
        {
            _commandLog = new SimulationLog(
                new SimulationLogHeader(_simulationSeed, _simulationTicks.StepMilliseconds, _simulationTicks.Tick));
            return _commandLog;
        }
    }

    /// <summary>
    /// Replays <paramref name="log"/> on this world, which must be built the way the recorded one was: same content,
    /// same seed, same starting tick. Commands go through the same ingress and are applied by the step that follows
    /// them in the log. <paramref name="setWallClock"/> moves this world's wall clock to each entry's sample. Throws at
    /// the first entry where the run diverges, naming it.
    /// </summary>
    internal async Task ReplayAsync(SimulationLog log, Action<long> setWallClock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(setWallClock);
        var expected = log.Header;
        var actual = new SimulationLogHeader(_simulationSeed, _simulationTicks.StepMilliseconds, _simulationTicks.Tick);
        if (expected != actual)
            throw new InvalidOperationException($"replay needs a world matching the recording: log {expected}, world {actual}");

        var submitted = new List<(CommandLogEntry Entry, Task<string?> Reply)>();
        foreach (var entry in log.Entries)
        {
            setWallClock(entry.WallUnixMs);
            switch (entry)
            {
                case CommandLogEntry command:
                    submitted.Add((command, ExecuteAsync(SessionOf(command), CommandOf(command), ct)));
                    break;
                case StepLogEntry:
                    StepSimulation();
                    if (_simulationTicks.Tick != entry.Tick)
                        throw Diverged(entry, $"the step reached tick {_simulationTicks.Tick}");
                    foreach (var (command, reply) in submitted)
                        await CheckReplayedAsync(command, reply, entry.Tick).ConfigureAwait(false);
                    submitted.Clear();
                    break;
                default:
                    throw new InvalidOperationException($"replay does not know log entry {entry}");
            }
        }

        if (submitted.Count > 0)
            throw Diverged(submitted[0].Entry, "the log ends before a step applied it");
    }

    ZoneSession SessionOf(CommandLogEntry entry)
    {
        using (_affinity.Step())
        {
            return _byId.TryGetValue(entry.ActorObjectId, out var actor) && actor.Session is { } live
                ? live
                : throw Diverged(entry, $"object {entry.ActorObjectId} has no session in this world");
        }
    }

    static ZoneCommand CommandOf(CommandLogEntry entry) =>
        entry.Command as ZoneCommand ?? throw Diverged(entry, $"{entry.Command.GetType().Name} is not a zone command");

    static async Task CheckReplayedAsync(CommandLogEntry entry, Task<string?> reply, long appliedTick)
    {
        if (entry.Tick != appliedTick)
            throw Diverged(entry, $"it was applied at tick {appliedTick}");
        string outcome;
        try
        {
            outcome = await reply.ConfigureAwait(false) ?? "ok";
        }
        catch (Exception ex)
        {
            outcome = $"threw {ex.GetType().Name}";
        }

        if (outcome != entry.Outcome)
            throw Diverged(entry, $"the command returned '{outcome}'");
    }

    static InvalidOperationException Diverged(SimulationLogEntry entry, string what) =>
        new($"replay diverged at entry {entry.Sequence} ({entry}): {what}");
}