using Fineage.Accounts;
using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>P1-8: ground journal accounts from a prior process must not outlive the world.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>After journal replay, expire any ground account with no live stack in the world.</summary>
    void ExpireOrphanGroundJournalAccounts()
    {
        foreach (var account in _ledger.NonEmptyGroundAccounts())
        {
            if (_byId.TryGetValue(account.OwnerId, out var live) && live.Kind == ZoneEntityKind.GroundItem)
                continue;
            var holdings = _ledger.HoldingsOf(account);
            if (holdings.IsEmpty)
                continue;
            var move = BeginLedgerMove(LedgerTxKind.Expire, 0, account.OwnerId);
            ExpireGroundHoldings(move, account, holdings);
            move.Commit(LedgerHoldingsOf);
            ZoneLog.Info($"ground journal expire object={account.OwnerId} on boot");
        }
    }

    static void ExpireGroundHoldings(LedgerMove move, LedgerAccount ground, LedgerHoldings holdings)
    {
        move.Observe(ground, holdings);
        var destroyed = LedgerAccount.Of(SystemAccount.Destroyed);
        foreach (var (itemId, count) in holdings.Counts)
        {
            if (count == 0)
                continue;
            move.Post(ground, itemId, -count);
            move.Post(destroyed, itemId, count);
        }

        foreach (var (instanceId, itemId) in holdings.Instances)
        {
            move.Post(ground, itemId, -1, [instanceId]);
            move.Post(destroyed, itemId, 1, [instanceId]);
        }
    }
}