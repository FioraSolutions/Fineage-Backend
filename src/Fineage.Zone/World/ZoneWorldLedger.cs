using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Game.Services;
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
    readonly ILedgerJournal _journal;
    readonly LedgerTxSequence _ledgerIds = new();

    internal EconomyLedger LedgerForTests => _ledger;

    internal LedgerHoldings LedgerHoldingsForTests(LedgerAccount account)
    {
        using (_affinity.Step())
            return LedgerHoldingsOf(account);
    }

    /// <summary>
    /// A move at the step's tick, numbered after the tick's earlier moves. A character with a durable
    /// economy commit in flight is not blocked here: the step keeps one scenario going across several
    /// moves (unlock, then the chest's own drop), and the in-flight batch is a snapshot the completion
    /// drain applies in order. Guarding moves on the reservation froze the second move of every such
    /// scenario and broke #948's allow-writes-during-commit contract.
    /// </summary>
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
            _byId.TryGetValue(account.OwnerId, out var player) && player.Inventory != null
                ? LedgerInventoryChange.HoldingsFrom(player.Inventory)
                : _accounts?.FindByCharacterId(account.OwnerId) is { Items: { } stored }
                    ? HoldingsFromStored(stored)
                    : throw new InvalidOperationException($"{account} has no inventory in the world or store"),
        { Owner: LedgerOwnerKind.Castle, Container: LedgerContainer.Treasury } =>
            new LedgerHoldings().Add(ShopTrade.AdenaItemId, _siege.Board.Find(account.OwnerId)?.Treasury
                ?? throw new InvalidOperationException($"{account} is not a castle")),
        { Owner: LedgerOwnerKind.World, Container: LedgerContainer.Ground } groundAccount =>
            GroundHoldingsInWorld(groundAccount),
        { Owner: LedgerOwnerKind.Character, Container: LedgerContainer.Warehouse } =>
            HoldingsOf(_byId.TryGetValue(account.OwnerId, out var owner) && owner.Warehouse != null
                ? owner.Warehouse
                : throw new InvalidOperationException($"{account} has no warehouse in the world")),
        { Owner: LedgerOwnerKind.Character, Container: LedgerContainer.Freight } freightAccount =>
            _byId.TryGetValue(freightAccount.OwnerId, out var freightOwner) && freightOwner.Freight != null
                ? HoldingsOf(freightOwner.Freight)
                : HoldingsFromStored(QueuedFreight(freightAccount.OwnerId)
                    ?? _accounts?.FindByCharacterId(freightAccount.OwnerId)?.Freight
                    ?? []),
        { Owner: LedgerOwnerKind.Clan, Container: LedgerContainer.Warehouse } =>
            HoldingsOf(_clan.TryGet(account.OwnerId, out var clan)
                ? ClanWarehouseOf(clan)!
                : throw new InvalidOperationException($"{account} is not a clan")),
        { Owner: LedgerOwnerKind.Pet, Container: LedgerContainer.Inventory } =>
            HoldingsOf(SummonedPet(account.OwnerId)?.Inventory
                ?? throw new InvalidOperationException($"{account} is not a summoned pet")),
        { Owner: LedgerOwnerKind.Character, Container: LedgerContainer.Progress } progress =>
            _byId.TryGetValue(progress.OwnerId, out var progressPlayer)
                ? ProgressHoldingsOf(progressPlayer)
                : throw new InvalidOperationException($"{account} has no character in the world"),
        _ => throw new InvalidOperationException($"{account} has no state in the zone")
    };

    static LedgerHoldings ProgressHoldingsOf(ZoneEntity player) =>
        new LedgerHoldings().Add(LedgerResourceIds.Sp, player.Sp).Add(LedgerResourceIds.Exp, player.Exp);

    static LedgerHoldings HoldingsFromStored(IReadOnlyList<StoredItem> items)
    {
        var holdings = new LedgerHoldings();
        foreach (var row in items)
        {
            if (row.Count <= 0)
                continue;
            if (row.Count == 1 && row.ObjectId > 0)
                holdings.AddPiece(row.ItemId, row.ObjectId);
            else
                holdings.Add(row.ItemId, row.Count);
        }

        return holdings;
    }

    /// <summary>
    /// Freight for a character who is not in the world is queued until the economy commit. Reconciliation has to
    /// see that queue: the store row is written in the same transaction as the ledger insert, which is after this.
    /// </summary>
    IReadOnlyList<StoredItem>? QueuedFreight(int characterId)
    {
        using (_economyLock.Enter())
        {
            for (int i = _dirtyOfflineFreight.Count - 1; i >= 0; i--)
            {
                if (_dirtyOfflineFreight[i].CharacterId == characterId)
                    return _dirtyOfflineFreight[i].Freight;
            }
        }

        return null;
    }

    static LedgerHoldings HoldingsOf(PlayerInventory inventory) => LedgerInventoryChange.HoldingsFrom(inventory);

    /// <summary>Delegates to <see cref="LedgerInventoryChange.PostChange"/> for partial-class ledger sites and tests.</summary>
    internal static void PostChange(
        LedgerMove move, LedgerAccount account, LedgerHoldings before, LedgerHoldings after, LedgerAccount counterpart) =>
        LedgerInventoryChange.PostChange(move, account, before, after, counterpart);

    static LedgerHoldings HoldingsOf(PrivateWarehouse warehouse)
    {
        var holdings = new LedgerHoldings();
        foreach (var stack in warehouse.Items)
            holdings.Add(stack.ItemId, stack.Count);
        return holdings;
    }

    /// <summary>
    /// Runs <paramref name="change"/>, which may change <paramref name="player"/>'s bag only, and records what it
    /// gained and lost against <paramref name="counterpart"/>. Nothing is recorded when the bag is unchanged.
    /// </summary>
    T RecordBagChange<T>(
        ZoneEntity player, LedgerTxKind kind, LedgerAccount counterpart, Func<T> change, Func<T, int> reference)
    {
        if (player.Inventory == null)
            return change();
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var before = LedgerInventoryChange.HoldingsFrom(player.Inventory);
        var result = change();
        var after = LedgerInventoryChange.HoldingsFrom(player.Inventory);
        if (after.SameAs(before))
            return result;
        var move = BeginLedgerMove(kind, player.ObjectId, reference(result));
        move.Observe(bag, before);
        LedgerInventoryChange.PostChange(move, bag, before, after, counterpart);
        move.Commit(LedgerHoldingsOf);
        return result;
    }

    QuestOutcome RecordQuest(ZoneEntity player, Func<QuestOutcome> step) => RecordBagChange(
        player, LedgerTxKind.QuestReward, LedgerAccount.Of(SystemAccount.Quests), step, outcome => outcome.QuestId);

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
    void PostShopMove(LedgerMove move, ZoneEntity player, ShopMutation mutation, IReadOnlyList<(int CastleId, long Amount)> taxes)
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
    }

    bool TryCommitShopMove(
        LedgerMove move,
        ZoneEntity player,
        ZoneSession session,
        ShopMutation mutation,
        IReadOnlyList<StoredItem> inventoryBackup,
        IReadOnlyList<(int CastleId, long Amount)> taxes)
    {
        PostShopMove(move, player, mutation, taxes);
        if (move.CheckCommit() != LedgerRejection.None)
        {
            ZoneLog.Warn($"shop move refused for object {player.ObjectId}: ledger rejected the postings");
            CharacterInventory.Replace(player.Inventory!, inventoryBackup);
            SendDeny(session, ShopMutation.Fail(ShopDeny.NotTradable));
            return false;
        }

        move.Commit(LedgerHoldingsOf);
        return true;
    }
}
