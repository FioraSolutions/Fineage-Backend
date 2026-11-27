namespace Fineage.Accounts;

/// <summary>
/// Result of a real PostgreSQL readiness probe. A false <see cref="Reachable"/> or
/// <see cref="MigrationsCurrent"/> always carries a concrete <see cref="Error"/>; no caller may
/// substitute a healthy default.
/// </summary>
public sealed record DatabaseReadiness(
    bool Reachable,
    bool MigrationsCurrent,
    int AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    bool LedgerReady,
    string? Error,
    string? MigrationError,
    string? LedgerError);
