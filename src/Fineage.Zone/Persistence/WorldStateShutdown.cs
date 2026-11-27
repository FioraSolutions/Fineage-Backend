namespace Fineage.Zone.Persistence;

/// <summary>
/// The closed list of stateful world subsystems with a JSON sidecar in <c>FINEAGE_STATE_DIR</c>.
/// Adding a subsystem means adding it here and to <see cref="ShutdownFlushRegistry"/>; the startup guard rejects a
/// registry that omits one, so a new sidecar cannot silently miss graceful shutdown.
/// </summary>
public static class WorldStateSystems
{
    public const string BossSchedule = "boss-schedule";
    public const string SevenSigns = "seven-signs";
    public const string Olympiad = "olympiad";
    public const string Lottery = "lottery";
    public const string RaidBossPoints = "raid-boss-points";
    public const string Punishment = "punishment";
    public const string MonsterRace = "monster-race";
    public const string FishingChampionship = "fishing-championship";

    public static IReadOnlyList<string> All { get; } =
    [
        BossSchedule,
        SevenSigns,
        Olympiad,
        Lottery,
        RaidBossPoints,
        Punishment,
        MonsterRace,
        FishingChampionship
    ];
}

/// <summary>
/// One registry that flushes every world sidecar during graceful shutdown. It owns no game logic: each subsystem
/// supplies its existing save operation unchanged, so Mobius semantics stay where they are.
/// </summary>
public sealed class ShutdownFlushRegistry
{
    readonly List<(string System, Action Flush)> _flush = [];
    readonly List<string> _lastFailures = [];

    public IReadOnlyList<string> RegisteredSystems => _flush.Select(entry => entry.System).ToArray();

    public IReadOnlyList<string> LastFailures => _lastFailures;

    public void Register(string system, Action flush)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentNullException.ThrowIfNull(flush);
        if (!WorldStateSystems.All.Contains(system, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stateful system '{system}' is not in WorldStateSystems.All. Add it to the closed list before registering it.");
        }
        if (_flush.Any(entry => string.Equals(entry.System, system, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Stateful system '{system}' is already registered for shutdown.");

        _flush.Add((system, flush));
    }

    /// <summary>Startup/test guard: every known stateful subsystem must have registered its shutdown flush.</summary>
    public void AssertComplete()
    {
        string[] missing = WorldStateSystems.All
            .Where(system => !_flush.Any(entry => string.Equals(entry.System, system, StringComparison.Ordinal)))
            .ToArray();
        if (missing.Length == 0)
            return;

        throw new InvalidOperationException(
            "Shutdown flush registry is incomplete; missing: " + string.Join(", ", missing) +
            ". Register each stateful subsystem so its sidecar is durable on graceful shutdown.");
    }

    /// <summary>
    /// Flushes every registered subsystem even when one fails. Failures are logged with the subsystem and the
    /// underlying path/system error; the caller keeps shutting the rest of the process down.
    /// </summary>
    public IReadOnlyList<string> FlushAll()
    {
        _lastFailures.Clear();
        foreach (var (system, flush) in _flush)
        {
            try
            {
                flush();
            }
            catch (Exception ex)
            {
                string failure = $"{system}: {ex.Message}";
                _lastFailures.Add(failure);
                ZoneLog.Error($"[state] shutdown flush failed for '{system}': {ex.Message}");
            }
        }

        return _lastFailures.ToArray();
    }
}
