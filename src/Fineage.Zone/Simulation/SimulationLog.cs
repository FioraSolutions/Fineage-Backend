namespace Fineage.Zone.Simulation;

/// <summary>An input the simulation applies between steps (a player's move, attack, skill use, …).</summary>
public interface ISimulationCommand
{
    string Name { get; }
}

/// <summary>
/// What a run needs to be replayed: the seed the random streams were built from, the fixed step and the tick the
/// recording started at.
/// </summary>
public sealed record SimulationLogHeader(ulong Seed, int StepMilliseconds, long StartTick);

/// <summary>One recorded input, in the order the simulation applied it. <paramref name="Tick"/> is the last tick the clock had reached.</summary>
public abstract record SimulationLogEntry(long Sequence, long Tick, long WallUnixMs);

/// <summary>A scheduler step that reached <see cref="SimulationLogEntry.Tick"/> from a wall sample of <see cref="SimulationLogEntry.WallUnixMs"/>.</summary>
public sealed record StepLogEntry(long Sequence, long Tick, long WallUnixMs)
    : SimulationLogEntry(Sequence, Tick, WallUnixMs);

/// <summary>
/// A command applied by <paramref name="ActorObjectId"/> after <see cref="SimulationLogEntry.Tick"/>, at
/// <see cref="SimulationLogEntry.WallUnixMs"/>, with the outcome it returned (<c>ok</c>, the rejection reason, or the
/// exception type it threw), so a replay can tell where it diverged.
/// </summary>
public sealed record CommandLogEntry(
    long Sequence,
    long Tick,
    long WallUnixMs,
    int ActorObjectId,
    ISimulationCommand Command,
    string Outcome) : SimulationLogEntry(Sequence, Tick, WallUnixMs);

/// <summary>
/// The per-tick input log of one run: every step and every command, in the order they took the world gate. Replaying
/// it on a world built the same way, with the same seed, reproduces the run's state and fan-out.
/// </summary>
public sealed class SimulationLog(SimulationLogHeader header)
{
    readonly List<SimulationLogEntry> _entries = [];

    public SimulationLogHeader Header { get; } = header ?? throw new ArgumentNullException(nameof(header));

    public IReadOnlyList<SimulationLogEntry> Entries
    {
        get
        {
            lock (_entries)
                return _entries.ToArray();
        }
    }

    public void AppendStep(long tick, long wallUnixMs)
    {
        lock (_entries)
            _entries.Add(new StepLogEntry(_entries.Count, tick, wallUnixMs));
    }

    public void AppendCommand(long tick, long wallUnixMs, int actorObjectId, ISimulationCommand command, string outcome)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_entries)
            _entries.Add(new CommandLogEntry(_entries.Count, tick, wallUnixMs, actorObjectId, command, outcome));
    }
}
