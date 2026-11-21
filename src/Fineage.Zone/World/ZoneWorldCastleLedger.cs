using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static readonly LedgerAccount CastleMaintenance = LedgerAccount.Of(SystemAccount.CastleMaintenance);
    static readonly LedgerAccount ManorCounterpart = LedgerAccount.Of(SystemAccount.Manor);
    static readonly LedgerAccount CastleGrants = LedgerAccount.Of(SystemAccount.CastleGrants);

    /// <summary>Posts adena between a character bag and a castle treasury after <paramref name="mutate"/>.</summary>
    string? RecordTreasuryExchange(
        ZoneSession session,
        ZoneEntity player,
        CastleSiege castle,
        LedgerTxKind kind,
        long amount,
        Action mutate)
    {
        if (player.Inventory == null || amount <= 0)
            return SiegeProtocol.Adena;
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var vault = LedgerAccount.Treasury(castle.Id);
        var move = BeginLedgerMove(kind, player.ObjectId, castle.Id);
        ObserveLedger(move, bag);
        ObserveLedger(move, vault);
        if (kind == LedgerTxKind.TreasuryDeposit)
        {
            move.Post(bag, PledgeProtocol.AdenaItemId, -amount);
            move.Post(vault, PledgeProtocol.AdenaItemId, amount);
        }
        else
        {
            move.Post(vault, PledgeProtocol.AdenaItemId, -amount);
            move.Post(bag, PledgeProtocol.AdenaItemId, amount);
        }

        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"treasury {kind} refused for object {player.ObjectId} castle {castle.Id}: ledger rejected the postings");
            return SiegeProtocol.Adena;
        }

        mutate();
        move.Commit(LedgerHoldingsOf);
        return null;
    }

    bool RecordCastleUpgrade(ZoneEntity player, int doorId, long price, Action mutate)
    {
        if (player.Inventory == null || price <= 0)
            return false;
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var move = BeginLedgerMove(LedgerTxKind.CastleUpgrade, player.ObjectId, doorId);
        ObserveLedger(move, bag);
        move.Post(bag, PledgeProtocol.AdenaItemId, -price);
        move.Post(CastleMaintenance, PledgeProtocol.AdenaItemId, price);
        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"castle upgrade refused for object {player.ObjectId} door {doorId}: ledger rejected the postings");
            return false;
        }

        mutate();
        move.Commit(LedgerHoldingsOf);
        return true;
    }

    void RecordTreasuryAdena(int castleId, LedgerTxKind kind, int reference, long delta, SystemAccount counterpart, Action mutate)
    {
        if (delta == 0)
        {
            mutate();
            return;
        }

        var vault = LedgerAccount.Treasury(castleId);
        var cp = LedgerAccount.Of(counterpart);
        var move = BeginLedgerMove(kind, 0, reference);
        ObserveLedger(move, vault);
        move.Post(vault, ShopTrade.AdenaItemId, delta);
        move.Post(cp, ShopTrade.AdenaItemId, -delta);
        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"treasury move {kind} refused for castle {castleId}: ledger rejected the postings");
            return;
        }

        mutate();
        move.Commit(LedgerHoldingsOf);
    }

    /// <summary>Player buys manor seeds: adena to treasury, seeds from the manor system into the bag.</summary>
    string? RecordManorSeedBuy(
        ZoneEntity player,
        int castleId,
        long totalAdena,
        IReadOnlyList<(int SeedId, long Count)> seeds,
        Action mutate)
    {
        if (player.Inventory == null || totalAdena <= 0 || seeds.Count == 0)
            return ManorProtocol.Adena;
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var vault = LedgerAccount.Treasury(castleId);
        var move = BeginLedgerMove(LedgerTxKind.ManorSeedBuy, player.ObjectId, castleId);
        ObserveLedger(move, bag);
        ObserveLedger(move, vault);
        move.Post(bag, PledgeProtocol.AdenaItemId, -totalAdena);
        move.Post(vault, PledgeProtocol.AdenaItemId, totalAdena);
        foreach (var (seedId, count) in seeds)
        {
            move.Post(ManorCounterpart, seedId, -count);
            move.Post(bag, seedId, count);
        }

        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"manor seed buy refused for object {player.ObjectId} castle {castleId}: ledger rejected the postings");
            return ManorProtocol.Adena;
        }

        mutate();
        move.Commit(LedgerHoldingsOf);
        return null;
    }
}