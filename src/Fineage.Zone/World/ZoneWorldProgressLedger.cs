using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>Auditable SP/EXP moves beside live character stats.</summary>
public sealed partial class ZoneWorld
{
    bool RecordProgressSpend(ZoneEntity player, LedgerTxKind kind, int reference, long spCost, long expCost, Action spend)
    {
        if (spCost <= 0 && expCost <= 0)
        {
            spend();
            return true;
        }

        var account = LedgerAccount.Progress(player.ObjectId);
        var before = ProgressHoldingsOf(player);
        var move = BeginLedgerMove(kind, player.ObjectId, reference);
        move.Observe(account, before);
        if (spCost > 0)
        {
            move.Post(account, LedgerResourceIds.Sp, -spCost);
            move.Post(Fees, LedgerResourceIds.Sp, spCost);
        }

        if (expCost > 0)
        {
            move.Post(account, LedgerResourceIds.Exp, -expCost);
            move.Post(Fees, LedgerResourceIds.Exp, expCost);
        }

        if (move.CheckCommit() != LedgerRejection.None)
            return false;
        spend();
        var after = ProgressHoldingsOf(player);
        if (after.SameAs(before))
            return true;
        move.Commit(acct => acct == account ? after : LedgerHoldingsOf(acct));
        return true;
    }
}