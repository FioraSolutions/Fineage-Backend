using System.Diagnostics;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.Simulation;
using Fineage.Zone;

namespace Fineage.Zone.World;

/// <summary>
/// Command ingress, the per-tick command log and its replay. See sections 3.3 and 4 of
/// <c>docs/architecture/zone-simulation.md</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    const string CommandsStage = "commands";

    readonly ZoneCommandIngress _ingress = new();
    SimulationLog? _commandLog;

    internal int SubmissionWakeCountForTest => _ingress.WakeSignalCountForTest;
    internal int CommandIngressQueuedDepth => _ingress.QueuedDepth;

    /// <summary>
    /// Submits <paramref name="command"/> for <paramref name="session"/>. It is applied in phase 0 of the next step at
    /// the tick time; the task completes with its rejection reason (or null) once that step's fan-out was sent.
    /// </summary>
    public Task<string?> ExecuteAsync(ZoneSession session, ZoneCommand command, CancellationToken ct) =>
        _ingress.Submit(session, command, ct);

    /// <summary>Phase 0: every command submitted before the step started, in submission order.</summary>
    void ApplyCommands(TickContext tick)
    {
        int countBudget = ZoneSimulationSettings.CommandsPerTick;
        int wallBudgetMs = ZoneSimulationSettings.CommandsStageBudgetMs;
        long wallDeadline = wallBudgetMs > 0
            ? Stopwatch.GetTimestamp() + (long)((Stopwatch.Frequency * (double)wallBudgetMs) / 1000d)
            : 0;

        while (true)
        {
            var batch = _ingress.TakeQueued(countBudget);
            if (batch.Count == 0)
                break;

            bool applied = false;
            var deferred = new List<ZoneCommandIngress.Submission>();
            int index = 0;
            for (; index < batch.Count; index++)
            {
                if (wallDeadline > 0 && Stopwatch.GetTimestamp() >= wallDeadline)
                    break;

                var submission = batch[index];

                // Cancelled by its connection before this tick: it never happened.
                if (submission.Reply.Task.IsCompleted)
                    continue;

                string? error = null;
                Exception? failure = null;
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

                if (submission.Command.Logged)
                    _commandLog?.AppendCommand(tick.Tick, tick.WallUnixMs, submission.Session.ObjectId, submission.Command,
                        failure != null ? $"threw {failure.GetType().Name}" : error ?? "ok");
                _ingress.Record(submission, error, failure);
                applied = true;
            }

            if (index < batch.Count)
                deferred.AddRange(batch.Skip(index));

            if (deferred.Count > 0)
                _ingress.PutBackFront(deferred);

            if (applied)
                ReleaseAppliedCommandReplies();

            if (wallDeadline > 0 && Stopwatch.GetTimestamp() >= wallDeadline)
                break;

            if (countBudget <= 0 || _ingress.QueuedDepth == 0)
                break;
        }
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
