using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Command execution, the per-tick command log and its replay. See section 4 of
/// <c>docs/architecture/zone-simulation.md</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    SimulationLog? _commandLog;

    /// <summary>
    /// Applies <paramref name="command"/> for <paramref name="session"/> under the world gate at one pinned time sample
    /// and appends it, with its outcome, to the command log when one is recording. Inventory Postgres writes the command
    /// dirtied are flushed after the gate.
    /// </summary>
    public Task<string?> ExecuteAsync(ZoneSession session, ZoneCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(command);
        return WithDeferredInventoryPersist(() =>
        {
            long wall = _simulationClock.WallUnixMs();
            string outcome = "ok";
            try
            {
                Task<string?> applied;
                using (_simulationClock.Pin(wall))
                    applied = command.ApplyAsync(this, session, ct);
                if (!applied.IsCompleted)
                    throw new InvalidOperationException(
                        $"command {command.Name} for object {session.ObjectId} did not complete synchronously under the world gate");
                string? error = applied.GetAwaiter().GetResult();
                outcome = error ?? "ok";
                return Task.FromResult(error);
            }
            catch (Exception ex)
            {
                outcome = $"threw {ex.GetType().Name}";
                throw;
            }
            finally
            {
                _commandLog?.AppendCommand(_simulationTicks.Tick, wall, session.ObjectId, command, outcome);
            }
        });
    }

    /// <summary>Starts recording every step and command of this world into a fresh log (tests and diagnostics).</summary>
    internal SimulationLog RecordSimulation()
    {
        lock (_gate)
        {
            _commandLog = new SimulationLog(
                new SimulationLogHeader(_simulationSeed, _simulationTicks.StepMilliseconds, _simulationTicks.Tick));
            return _commandLog;
        }
    }

    /// <summary>
    /// Replays <paramref name="log"/> on this world, which must be built the way the recorded one was: same content,
    /// same seed, same starting tick. <paramref name="setWallClock"/> moves this world's wall clock to each entry's
    /// sample. Throws at the first entry where the run diverges, naming it.
    /// </summary>
    internal async Task ReplayAsync(SimulationLog log, Action<long> setWallClock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(setWallClock);
        var expected = log.Header;
        var actual = new SimulationLogHeader(_simulationSeed, _simulationTicks.StepMilliseconds, _simulationTicks.Tick);
        if (expected != actual)
            throw new InvalidOperationException($"replay needs a world matching the recording: log {expected}, world {actual}");

        foreach (var entry in log.Entries)
        {
            setWallClock(entry.WallUnixMs);
            switch (entry)
            {
                case StepLogEntry:
                    StepSimulation();
                    if (_simulationTicks.Tick != entry.Tick)
                        throw Diverged(entry, $"the step reached tick {_simulationTicks.Tick}");
                    break;
                case CommandLogEntry command:
                    await ReplayCommandAsync(command, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException($"replay does not know log entry {entry}");
            }
        }
    }

    async Task ReplayCommandAsync(CommandLogEntry entry, CancellationToken ct)
    {
        if (_simulationTicks.Tick != entry.Tick)
            throw Diverged(entry, $"the world is at tick {_simulationTicks.Tick}");
        if (entry.Command is not ZoneCommand command)
            throw Diverged(entry, $"{entry.Command.GetType().Name} is not a zone command");
        ZoneSession session;
        lock (_gate)
        {
            session = _byId.TryGetValue(entry.ActorObjectId, out var actor) && actor.Session is { } live
                ? live
                : throw Diverged(entry, $"object {entry.ActorObjectId} has no session in this world");
        }

        string outcome;
        try
        {
            outcome = await ExecuteAsync(session, command, ct).ConfigureAwait(false) ?? "ok";
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
