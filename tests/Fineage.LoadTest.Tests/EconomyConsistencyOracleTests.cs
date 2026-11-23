using Fineage.LoadTest.Consistency;
using Fineage.LoadTest.Metrics;
using Fineage.LoadTest.Models;
using Xunit;

namespace Fineage.LoadTest.Tests;

public sealed class EconomyConsistencyOracleTests
{
    [Fact]
    public void Ledger_invariant_metric_fails_when_required()
    {
        var scenario = new LoadScenario
        {
            Economy = new EconomyHammerConfig { Enabled = true, RequireLedgerInvariant = true }
        };
        var metrics = new List<PrometheusSample>
        {
            new() { LedgerInvariantViolations = 2, LedgerFlushFailures = 0 }
        };

        var report = EconomyConsistencyOracle.Analyze([], scenario, metrics, null);

        Assert.Contains(report.Issues, i => i.Kind == EconomyConsistencyKind.LedgerInvariantViolation);
        Assert.True(EconomyConsistencyOracle.IsHardFailure(EconomyConsistencyKind.LedgerInvariantViolation));
    }

    [Fact]
    public void Parser_reads_ledger_invariant_counter()
    {
        const string text = """
            fineage_ledger_invariant_violations_total 3
            fineage_ledger_flush_failures_total 0
            """;
        var sample = PrometheusParser.Parse(text);
        Assert.Equal(3, sample.LedgerInvariantViolations);
    }
}