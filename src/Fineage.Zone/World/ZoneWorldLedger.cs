using Fineage.Game.Services;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Shops;

namespace Fineage.Zone.World;

/// <summary>
/// The auditable economy ledger beside the snapshot writes (step 11): inventories and treasuries still decide every
/// move and are still saved as before; each migrated move is also recorded as a ledger transaction and reconciled
/// against them (<see cref="LedgerMove"/>).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>How long a transaction id stays in the ledger's idempotency index.</summary>
    const long LedgerIdempotencyMilliseconds = 60 * 60 * 1000;

    readonly EconomyLedger _ledger;
    readonly LedgerTxSequence _ledgerIds = new();

    internal EconomyLedger LedgerForTests => _ledger;

    internal LedgerHoldings LedgerHoldingsForTests(LedgerAccount account)
    {
        lock (_gate)
            return LedgerHoldingsOf(account);
    }

    /// <summary>A move at the step's tick, numbered after the tick's earlier moves.</summary>
    LedgerMove BeginLedgerMove(LedgerTxKind kind, int causedBy, int reference)
    {
        if (RegionBodyScope.Of(this) != null)
            throw new InvalidOperationException(
                $"{kind} from a region body: ledger moves are numbered on the scheduler thread; defer it with AtBarrier");
        long tick = _simulationClock.TickAt(NowUnixMs());
        return new LedgerMove(_ledger, () => _ledgerIds.Next(tick), kind, _simulationClock.WallUnixMs(), causedBy, reference);
    }

    LedgerHoldings LedgerHoldingsOf(LedgerAccount account) => account switch
    {
        { Owner: LedgerOwnerKind.Character, Container: LedgerContainer.Inventory } =>
            HoldingsOf(_byId.TryGetValue(account.OwnerId, out var player) && player.Inventory != null
                ? player.Inventory
                : throw new InvalidOperationException($"{account} has no inventory in the world")),
        { Owner: LedgerOwnerKind.Castle, Container: LedgerContainer.Treasury } =>
            new LedgerHoldings().Add(ShopTrade.AdenaItemId, _siege.Board.Find(account.OwnerId)?.Treasury
                ?? throw new InvalidOperationException($"{account} is not a castle")),
        { Owner: LedgerOwnerKind.World, Container: LedgerContainer.Ground } =>
            _byId.TryGetValue(account.OwnerId, out var ground) && ground.Kind == ZoneEntityKind.GroundItem
                ? GroundHoldingsOf(ground)
                : new LedgerHoldings(),
        { Owner: LedgerOwnerKind.Character, Container: LedgerContainer.Warehouse } =>
            HoldingsOf(_byId.TryGetValue(account.OwnerId, out var owner) && owner.Warehouse != null
                ? owner.Warehouse
                : throw new InvalidOperationException($"{account} has no warehouse in the world")),
        { Owner: LedgerOwnerKind.Clan, Container: LedgerContainer.Warehouse } =>
            HoldingsOf(_clan.TryGet(account.OwnerId, out var clan)
                ? ClanWarehouseOf(clan)!
                : throw new InvalidOperationException($"{account} is not a clan")),
        { Owner: LedgerOwnerKind.Pet, Container: LedgerContainer.Inventory } =>
            HoldingsOf(SummonedPet(account.OwnerId)?.Inventory
                ?? throw new InvalidOperationException($"{account} is not a summoned pet")),
        _ => throw new InvalidOperationException($"{account} has no state in the zone")
    };

    static LedgerHoldings HoldingsOf(PlayerInventory inventory)
    {
        var holdings = new LedgerHoldings();
        foreach (var item in inventory.Items)
        {
            if (item.Template.Stackable)
                holdings.Add(item.ItemId, item.Count);
            else if (item.Count == 1)
                holdings.AddPiece(item.ItemId, item.ObjectId);
            else
                throw new InvalidOperationException($"non-stackable item {item.ObjectId} has count {item.Count}");
        }

        return holdings;
    }

    static LedgerHoldings HoldingsOf(PrivateWarehouse warehouse)
    {
        var holdings = new LedgerHoldings();
        foreach (var stack in warehouse.Items)
            holdings.Add(stack.ItemId, stack.Count);
        return holdings;
    }

    void ObserveLedger(LedgerMove move, LedgerAccount account) => move.Observe(account, LedgerHoldingsOf(account));

    /// <summary>Opens a shop move: the buyer's inventory and every treasury the shop's tax can reach.</summary>
    LedgerMove BeginShopMove(LedgerTxKind kind, ZoneEntity player, int npcId, int castleId)
    {
        var move = BeginLedgerMove(kind, player.ObjectId, npcId);
        ObserveLedger(move, LedgerAccount.Inventory(player.ObjectId));
        foreach (int castle in TaxedTreasuries(castleId))
            ObserveLedger(move, LedgerAccount.Treasury(castle));
        return move;
    }

    /// <summary>
    /// The NPC shop is the counterparty of everything the player gained or gave; castle tax leaves the shop for the
    /// treasuries it was credited to.
    /// </summary>
    void CommitShopMove(LedgerMove move, ZoneEntity player, ShopMutation mutation, IReadOnlyList<(int CastleId, long Amount)> taxes)
    {
        var buyer = LedgerAccount.Inventory(player.ObjectId);
        var shop = LedgerAccount.Of(SystemAccount.NpcShop);
        foreach (var gained in mutation.Gained)
        {
            move.Post(buyer, gained.ItemId, gained.Count, gained.InstanceIds);
            move.Post(shop, gained.ItemId, -gained.Count);
        }

        foreach (var lost in mutation.Lost)
        {
            move.Post(buyer, lost.ItemId, -lost.Count, lost.InstanceIds);
            move.Post(shop, lost.ItemId, lost.Count);
        }

        foreach (var (castle, amount) in taxes)
        {
            move.Post(shop, ShopTrade.AdenaItemId, -amount);
            move.Post(LedgerAccount.Treasury(castle), ShopTrade.AdenaItemId, amount);
        }

        move.Commit(LedgerHoldingsOf);
    }
}
