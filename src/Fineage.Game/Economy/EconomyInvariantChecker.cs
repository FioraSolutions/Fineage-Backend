namespace Fineage.Game.Economy;

/// <summary>
/// Read-only checks on the auditable economy ledger: one owner per piece, non-system accounts never overdraft,
/// instance maps agree with holdings. Violations are reported with concrete entity ids; nothing is corrected.
/// </summary>
public static class EconomyInvariantChecker
{
    public sealed class Violation
    {
        public required string Code { get; init; }
        public required string Message { get; init; }
    }

    /// <summary>Runs ledger-only invariants against current projected balances.</summary>
    public static IReadOnlyList<Violation> Check(EconomyLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var issues = new List<Violation>();
        var accounts = ledger.NonEmptyAccounts();

        foreach (var (account, holdings) in accounts)
        {
            if (account.IsSystem)
                continue;
            foreach (var (itemId, count) in holdings.Counts)
            {
                if (count < 0)
                {
                    issues.Add(new Violation
                    {
                        Code = "overdraft",
                        Message = $"{account} holds negative count item={itemId} amount={count}"
                    });
                }
            }

            foreach (var (instance, itemId) in holdings.Instances)
            {
                if (ledger.OwnerOf(instance) is not { } owner)
                {
                    issues.Add(new Violation
                    {
                        Code = "orphan_instance",
                        Message = $"{account} holds instance={instance} item={itemId} but OwnerOf is missing"
                    });
                }
                else if (owner != account)
                {
                    issues.Add(new Violation
                    {
                        Code = "instance_owner_mismatch",
                        Message = $"instance={instance} item={itemId} held on {account} but OwnerOf={owner}"
                    });
                }
            }
        }

        int maxInstance = ledger.MaxInstanceId;
        for (int instance = 1; instance <= maxInstance; instance++)
        {
            if (ledger.OwnerOf(instance) is not { } owner)
                continue;
            var holdings = ledger.HoldingsOf(owner);
            if (!holdings.Instances.ContainsKey(instance))
            {
                issues.Add(new Violation
                {
                    Code = "stale_owner_index",
                    Message = $"OwnerOf(instance={instance})={owner} but holdings omit that instance"
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> on the first violation. Use in tests and live diagnostics.
    /// </summary>
    public static void AssertHold(EconomyLedger ledger)
    {
        var violation = Check(ledger).FirstOrDefault();
        if (violation != null)
            throw new InvalidOperationException(
                $"auditable economy ledger invariant {violation.Code}: {violation.Message}");
    }
}