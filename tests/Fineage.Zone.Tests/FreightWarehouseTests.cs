using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class FreightWarehouseTests
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
    public async Task Package_send_to_alt_and_freight_withdraw_posts_ledgers()
    {
        var bench = Open();
        var sender = await bench.EnterAsync(
            "acct", "Sender",
            items: [new StoredItem(Adena, 5000, -1), new StoredItem(Scroll, 3, -1)],
            x: 0, y: 0, z: 0);
        Assert.Equal(CreateCharacterStatus.Ok, bench.Store.TryAddCharacter(
            "acct", "Receiver", HumanClasses.Fighter, 0, 0, out var created,
            [new StoredItem(Adena, 100, -1)], hasPosition: true, x: 0, y: 0, z: 0));
        int receiverId = created!.Id;

        Assert.Null(await sender.BypassAsync(KeeperObjectId, "package_deposit"));
        var scrolls = sender.Player.Inventory!.Items.Single(item => item.ItemId == Scroll);
        Assert.Null(await sender.World.TryPackageSendAsync(
            sender.Session,
            receiverId,
            [new WarehouseLine(scrolls.ObjectId, 2)],
            default));

        int fee = FreightRules.DepositFee(1);
        var bag = LedgerAccount.Inventory(sender.Player.ObjectId);
        var freight = LedgerAccount.Freight(receiverId);
        var deposit = Only(bench, LedgerTxKind.FreightDeposit);
        Assert.Equal((sender.Player.ObjectId, receiverId), (deposit.CausedBy, deposit.Reference));
        Assert.Equal(
            new[]
            {
                Line(bag, Adena, -fee), Line(Fees, Adena, fee),
                Line(bag, Scroll, -2), Line(freight, Scroll, 2)
            }.Order(),
            Lines(deposit));

        var stored = bench.Store.FindById("acct", receiverId)!.Freight!;
        Assert.Equal(2, stored.Single(item => item.ItemId == Scroll).Count);

        var receiver = await bench.EnterAsync("acct", "Receiver", x: 0, y: 0, z: 0);
        Assert.Null(await receiver.BypassAsync(KeeperObjectId, "package_withdraw"));
        var stack = receiver.Player.Freight!.Items.Single(s => s.ItemId == Scroll);
        Assert.Null(await receiver.Withdraw(KeeperObjectId, (stack.ObjectId, 2)));

        var withdraw = Only(bench, LedgerTxKind.FreightWithdraw);
        Assert.Equal(
            new[] { Line(LedgerAccount.Inventory(receiver.Player.ObjectId), Scroll, 2), Line(freight, Scroll, -2) }.Order(),
            Lines(withdraw));
        Assert.Equal(2, receiver.Player.Inventory!.CountOf(Scroll));
    }

    [Fact]
    public async Task Refused_freight_withdraw_leaves_bag_and_freight_untouched()
    {
        var bench = Open();
        var sender = await bench.EnterAsync(
            "acct", "Sender", items: [new StoredItem(Adena, 5000, -1), new StoredItem(Harvester, 1, -1)], x: 0, y: 0, z: 0);
        Assert.Equal(CreateCharacterStatus.Ok, bench.Store.TryAddCharacter(
            "acct", "Receiver", HumanClasses.Fighter, 0, 0, out var created,
            [new StoredItem(Adena, 100, -1)], hasPosition: true, x: 0, y: 0, z: 0));
        Assert.Null(await sender.BypassAsync(KeeperObjectId, "package_deposit"));
        Assert.Null(await sender.World.TryPackageSendAsync(
            sender.Session, created!.Id,
            [new WarehouseLine(sender.Player.Inventory!.Items.Single(item => item.ItemId == Harvester).ObjectId, 1)], default));
        var receiver = await bench.EnterAsync("acct", "Receiver", x: 0, y: 0, z: 0);
        var bag = CharacterInventory.Rows(receiver.Player.Inventory!);
        var storedRows = receiver.Player.Freight!.Items.Select(item => (item.ObjectId, item.ItemId, item.Count)).ToArray();
        int collision = bench.World.NextItemObjectIdForTests;
        long tick = bench.Ledger.Entries.Count > 0 ? bench.Ledger.Entries[^1].Transaction.Id.Tick + 1 : 1;
        Assert.True(bench.World.LedgerForTests.Submit(new LedgerTransaction(
            new LedgerTxId(tick, 0), LedgerTxKind.Adjustment, 0, 0, 0,
            [new LedgerPosting(Fees, Harvester, -1), new LedgerPosting(LedgerAccount.Inventory(999_999), Harvester, 1, [collision])])).Ok);

        Assert.Null(await receiver.BypassAsync(KeeperObjectId, "package_withdraw"));
        var stack = receiver.Player.Freight.Items.Single(item => item.ItemId == Harvester);
        Assert.Equal("ledger_refused", await receiver.Withdraw(KeeperObjectId, (stack.ObjectId, 1)));
        Assert.Equal(bag, CharacterInventory.Rows(receiver.Player.Inventory));
        Assert.Equal(storedRows, receiver.Player.Freight.Items.Select(item => (item.ObjectId, item.ItemId, item.Count)));
        Assert.DoesNotContain(bench.Ledger.Entries, entry => entry.Transaction.Kind == LedgerTxKind.FreightWithdraw);
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