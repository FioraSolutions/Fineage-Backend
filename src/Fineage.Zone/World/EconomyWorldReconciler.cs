using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>
/// Ledger invariants plus world projections: ground stacks, character inventories online in the zone.
/// </summary>
public static class EconomyWorldReconciler
{
    public static IReadOnlyList<EconomyInvariantChecker.Violation> Check(ZoneWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var issues = new List<EconomyInvariantChecker.Violation>(EconomyInvariantChecker.Check(world.LedgerForTests));
        foreach (var (account, _) in world.LedgerForTests.NonEmptyAccounts())
        {
            if (account.Container != LedgerContainer.Ground)
                continue;
            if (!world.TryGetEntityById(account.OwnerId, out var ground))
            {
                issues.Add(new EconomyInvariantChecker.Violation
                {
                    Code = "ground_missing_entity",
                    Message = $"ledger ground account {account} has holdings but no live ground entity",
                });
                continue;
            }

            if (ground.Kind != ZoneEntityKind.GroundItem)
            {
                issues.Add(new EconomyInvariantChecker.Violation
                {
                    Code = "ground_not_stack",
                    Message = $"entity {account.OwnerId} is {ground.Kind}, not a ground stack",
                });
            }
        }

        foreach (var player in world.PlayersForTests())
        {
            if (player.Inventory == null)
                continue;
            var acct = LedgerAccount.Inventory(player.ObjectId);
            var ledger = world.LedgerForTests.HoldingsOf(acct);
            var live = LedgerInventoryChange.HoldingsFrom(player.Inventory);
            if (!ledger.SameAs(live))
            {
                issues.Add(new EconomyInvariantChecker.Violation
                {
                    Code = "inventory_projection_skew",
                    Message = $"char={player.ObjectId} bag={live} ledger={ledger}",
                });
            }
        }

        return issues;
    }
}