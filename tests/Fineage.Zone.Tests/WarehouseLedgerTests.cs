using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Private and clan warehouses on the auditable economy ledger (step 11d): deposits pay the fee to the fees account,
/// and warehouses hold a count per item because their stacks have no durable piece id.
/// </summary>
public sealed class WarehouseLedgerTests
{
    const int KeeperObjectId = 61001;
    const int Adena = 57;
    const int Scroll = 736;
    const int Harvester = 5125;

    static readonly LedgerAccount Fees = LedgerAccount.Of(SystemAccount.Fees);

    static LedgerTransaction Only(WorldBench bench, LedgerTxKind kind) =>
        Assert.Single(bench.Ledger.Entries, entry => entry.Transaction.Kind == kind).Transaction;

    static List<string> Lines(LedgerTransaction tx) => tx.Postings
        .Select(posting => $"{posting.Account} {posting.ItemId} {posting.Amount} [{string.Join(",", posting.InstanceIds)}]")
        .Order()
        .ToList();

    static string Line(LedgerAccount account, int itemId, long amount, params int[] pieces) =>
        $"{account} {itemId} {amount} [{string.Join(",", pieces)}]";

    [Fact]
    public async Task Private_deposit_pays_the_fee_and_a_withdrawn_piece_starts_a_new_id()
    {
        var bench = Open();
        var actor = await bench.EnterAsync(
            "keep", "Keep",
            items: [new StoredItem(Adena, 200, -1), new StoredItem(Scroll, 5, -1), new StoredItem(Harvester, 1, -1)],
            x: 0, y: 0, z: 0);
        var bag = LedgerAccount.Inventory(actor.Player.ObjectId);
        var store = LedgerAccount.Warehouse(actor.Player.ObjectId);
        var scrolls = actor.Player.Inventory!.Items.Single(item => item.ItemId == Scroll);
        int piece = actor.Player.Inventory.Items.Single(item => item.ItemId == Harvester).ObjectId;

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "DepositP"));
        Assert.Null(await actor.Deposit(KeeperObjectId, (scrolls.ObjectId, 3), (piece, 1)));

        int fee = WarehouseRules.DepositFee(2);
        var deposit = Only(bench, LedgerTxKind.WarehouseDeposit);
        Assert.Equal((actor.Player.ObjectId, 30005), (deposit.CausedBy, deposit.Reference));
        Assert.Equal(
            new[]
            {
                Line(bag, Adena, -fee), Line(Fees, Adena, fee),
                Line(bag, Scroll, -3), Line(store, Scroll, 3),
                Line(bag, Harvester, -1, piece), Line(store, Harvester, 1)
            }.Order(),
            Lines(deposit));
        Assert.Null(bench.World.LedgerForTests.OwnerOf(piece));

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "WithdrawP"));
        var stored = actor.Player.Warehouse!.Items.Single(stack => stack.ItemId == Harvester);
        Assert.Null(await actor.Withdraw(KeeperObjectId, (stored.ObjectId, 1)));

        int back = actor.Player.Inventory.Items.Single(item => item.ItemId == Harvester).ObjectId;
        var withdraw = Only(bench, LedgerTxKind.WarehouseWithdraw);
        Assert.Equal([Line(bag, Harvester, 1, back), Line(store, Harvester, -1)], Lines(withdraw));
        Assert.Equal(bag, bench.World.LedgerForTests.OwnerOf(back));
        Assert.True(bench.World.LedgerForTests.HoldingsOf(store).SameAs(new LedgerHoldings().Add(Scroll, 3)));
    }

    [Fact]
    public async Task Clan_deposit_and_leader_withdrawal_move_adena_through_the_clan_warehouse()
    {
        var bench = Open();
        var leader = await bench.EnterAsync(
            "lead", "Lead", items: [new StoredItem(Adena, 2000, -1)], level: 40, x: 0, y: 0, z: 0);
        Assert.Null(await bench.World.TryClanCreateAsync(leader.Session, new ClanCreateRequestMsg { Name = "Vault" }, bench.Now(), default));
        int clanId = leader.Player.ClanId;
        Assert.True(bench.World.SetClanLevelForTest(clanId, 1));
        var bag = LedgerAccount.Inventory(leader.Player.ObjectId);
        var vault = LedgerAccount.ClanWarehouse(clanId);
        int adena = leader.Player.Inventory!.Items.Single(item => item.ItemId == Adena).ObjectId;

        Assert.Null(await leader.BypassAsync(KeeperObjectId, "DepositC"));
        Assert.Null(await leader.Deposit(KeeperObjectId, (adena, 1000)));
        int fee = WarehouseRules.DepositFee(1);
        Assert.Equal(
            new[] { Line(bag, Adena, -1000 - fee), Line(Fees, Adena, fee), Line(vault, Adena, 1000) }.Order(),
            Lines(Only(bench, LedgerTxKind.WarehouseDeposit)));

        Assert.Null(await leader.BypassAsync(KeeperObjectId, "WithdrawC"));
        int stack = bench.World.ClanWarehouseStacksForTest(clanId).Single();
        Assert.Null(await leader.Withdraw(KeeperObjectId, (stack, 400)));
        Assert.Equal(
            new[] { Line(bag, Adena, 400), Line(vault, Adena, -400) }.Order(),
            Lines(Only(bench, LedgerTxKind.WarehouseWithdraw)));
        Assert.Equal(600, bench.World.LedgerForTests.HoldingsOf(vault).CountOf(Adena));
        Assert.Equal(600, bench.World.ClanWarehouseForTest(clanId)[Adena]);
    }

    static WorldBench Open()
    {
        var map = new SpawnMapDefinition(
            SpawnCatalog.DefaultMapId,
            true,
            [
                new SpawnNpcDefinition(
                    KeeperObjectId, 30005, "Wilford", 0, 0, 0, 0, false, 100, 80f, 40f, 60, 0,
                    "Warehouse Keeper", "Warehouse")
            ]);
        return new WorldBench(
            spawns: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [SpawnCatalog.DefaultMapId] = map }));
    }
}
