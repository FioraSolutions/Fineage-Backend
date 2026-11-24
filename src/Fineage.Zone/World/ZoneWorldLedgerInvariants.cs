using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>Live auditable economy ledger invariant checks for metrics and load-test gates.</summary>
public sealed partial class ZoneWorld
{
    long _lastLedgerInvariantCheckMs;
    string? _lastLedgerInvariantMessage;

    /// <summary>Runs <see cref="EconomyInvariantChecker"/> at most once per second per metrics scrape.</summary>
    public void PublishLedgerInvariantsForMetrics(long wallUnixMs)
    {
        if (wallUnixMs - _lastLedgerInvariantCheckMs < 1000)
            return;
        _lastLedgerInvariantCheckMs = wallUnixMs;
        var violations = EconomyInvariantChecker.Check(_ledger);
        if (violations.Count == 0)
        {
            _lastLedgerInvariantMessage = null;
            return;
        }

        foreach (var violation in violations)
        {
            _lastLedgerInvariantMessage = $"{violation.Code}: {violation.Message}";
            ZoneLog.Error($"auditable economy ledger invariant {violation.Code}: {violation.Message}");
            Fineage.Protocol.Telemetry.FineageTelemetry.LedgerInvariantViolations.Add(1,
                new KeyValuePair<string, object?>("code", violation.Code));
        }
    }

    internal string? LastLedgerInvariantMessageForTests => _lastLedgerInvariantMessage;
}