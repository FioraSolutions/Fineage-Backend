using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// B6 warehouse economy invariants: deposit/withdraw must not invent a second move of the same stack
/// (double-spend / over-count). Complements the fee/weight/persist coverage in <see cref="NetNpcServiceTests"/>.
/// </summary>
public sealed class NetWarehouseEconomyTests
{
    const int KeeperObjectId = 61001;
    const int Adena = 57;
    const int Scroll = 736;

    [Fact]
    public async Task Deposit_count_above_owned_stack_is_refused_and_leaves_bag_untouched()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync(
            "keep", "Keep",
            items: [new StoredItem(Adena, 200, -1), new StoredItem(Scroll, 5, -1)],
            x: 0, y: 0, z: 0);

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "DepositP"));
        var window = Assert.IsType<Fineage.Protocol.WarehouseDepositListMsg>(Last(bench, FanoutWarehouseDepositList)[1]);
        var scrolls = Assert.Single(window.Items, item => item.ItemId == Scroll);

        Assert.Null(await actor.Deposit(KeeperObjectId, (scrolls.ObjectId, scrolls.Count + 1)));
        Assert.Equal(NpcServiceProtocol.CannotStore, LastSystemMessageId(bench));
        Assert.Equal(5, actor.Player.Inventory!.Find(scrolls.ObjectId)!.Count);
        Assert.Empty(actor.Player.Warehouse!.Items);
        Assert.Equal(200, actor.Player.Inventory.CountOf(Adena));
    }

    [Fact]
    public async Task Duplicate_object_id_in_one_deposit_request_is_refused()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync(
            "keep", "Keep",
            items: [new StoredItem(Adena, 200, -1), new StoredItem(Scroll, 10, -1)],
            x: 0, y: 0, z: 0);

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "DepositP"));
        var window = Assert.IsType<Fineage.Protocol.WarehouseDepositListMsg>(Last(bench, FanoutWarehouseDepositList)[1]);
        var scrolls = Assert.Single(window.Items, item => item.ItemId == Scroll);

        Assert.Equal(
            "bad_request",
            await actor.Deposit(KeeperObjectId, (scrolls.ObjectId, 1), (scrolls.ObjectId, 1)));
        Assert.Equal(10, actor.Player.Inventory!.Find(scrolls.ObjectId)!.Count);
        Assert.Empty(actor.Player.Warehouse!.Items);
        Assert.Equal(200, actor.Player.Inventory.CountOf(Adena));
    }

    [Fact]
    public async Task Second_deposit_of_the_same_stack_after_move_cannot_invent_more_rows()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync(
            "keep", "Keep",
            items: [new StoredItem(Adena, 200, -1), new StoredItem(Scroll, 10, -1)],
            x: 0, y: 0, z: 0);

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "DepositP"));
        var window = Assert.IsType<Fineage.Protocol.WarehouseDepositListMsg>(Last(bench, FanoutWarehouseDepositList)[1]);
        var scrolls = Assert.Single(window.Items, item => item.ItemId == Scroll);
        int scrollObjectId = scrolls.ObjectId;

        Assert.Null(await actor.Deposit(KeeperObjectId, (scrollObjectId, 10)));
        Assert.Equal(NpcServiceProtocol.TransactionComplete, LastSystemMessageId(bench));
        Assert.Null(actor.Player.Inventory!.Find(scrollObjectId));
        Assert.Equal(10, Assert.Single(actor.Player.Warehouse!.Items, row => row.ItemId == Scroll).Count);
        Assert.Equal(170, actor.Player.Inventory.CountOf(Adena));

        // Replay the stale inventory object id — must not mint a second warehouse stack.
        Assert.Null(await actor.Deposit(KeeperObjectId, (scrollObjectId, 10)));
        Assert.Equal(NpcServiceProtocol.CannotStore, LastSystemMessageId(bench));
        Assert.Single(actor.Player.Warehouse.Items, row => row.ItemId == Scroll && row.Count == 10);
        Assert.Equal(170, actor.Player.Inventory.CountOf(Adena));
    }

    [Fact]
    public async Task Withdraw_count_above_stored_stack_is_refused()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync(
            "keep", "Keep",
            items: [new StoredItem(Adena, 200, -1), new StoredItem(Scroll, 10, -1)],
            x: 0, y: 0, z: 0);

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "DepositP"));
        var deposit = Assert.IsType<Fineage.Protocol.WarehouseDepositListMsg>(Last(bench, FanoutWarehouseDepositList)[1]);
        var scrolls = Assert.Single(deposit.Items, item => item.ItemId == Scroll);
        Assert.Null(await actor.Deposit(KeeperObjectId, (scrolls.ObjectId, 10)));

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "WithdrawP"));
        var withdraw = Assert.IsType<Fineage.Protocol.WarehouseWithdrawListMsg>(Last(bench, FanoutWarehouseWithdrawList)[1]);
        var stored = Assert.Single(withdraw.Items, item => item.ItemId == Scroll);

        Assert.Equal(
            "bad_request",
            await actor.Withdraw(KeeperObjectId, (stored.ObjectId, stored.Count + 1)));
        Assert.Equal(10, Assert.Single(actor.Player.Warehouse!.Items, row => row.ItemId == Scroll).Count);
        Assert.DoesNotContain(actor.Player.Inventory!.Items, item => item.ItemId == Scroll);
    }

    [Fact]
    public async Task Second_withdraw_of_the_same_stack_after_move_cannot_duplicate_into_bag()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync(
            "keep", "Keep",
            items: [new StoredItem(Adena, 200, -1), new StoredItem(Scroll, 4, -1)],
            x: 0, y: 0, z: 0);

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "DepositP"));
        var deposit = Assert.IsType<Fineage.Protocol.WarehouseDepositListMsg>(Last(bench, FanoutWarehouseDepositList)[1]);
        var scrolls = Assert.Single(deposit.Items, item => item.ItemId == Scroll);
        Assert.Null(await actor.Deposit(KeeperObjectId, (scrolls.ObjectId, 4)));

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "WithdrawP"));
        var withdraw = Assert.IsType<Fineage.Protocol.WarehouseWithdrawListMsg>(Last(bench, FanoutWarehouseWithdrawList)[1]);
        var stored = Assert.Single(withdraw.Items, item => item.ItemId == Scroll);
        int warehouseObjectId = stored.ObjectId;

        Assert.Null(await actor.Withdraw(KeeperObjectId, (warehouseObjectId, 4)));
        Assert.Equal(NpcServiceProtocol.TransactionComplete, LastSystemMessageId(bench));
        Assert.Empty(actor.Player.Warehouse!.Items);
        Assert.Equal(4, actor.Player.Inventory!.CountOf(Scroll));

        Assert.Equal(
            "bad_request",
            await actor.Withdraw(KeeperObjectId, (warehouseObjectId, 4)));
        Assert.Empty(actor.Player.Warehouse.Items);
        Assert.Equal(4, actor.Player.Inventory.CountOf(Scroll));
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

    static object?[] Last(WorldBench bench, string method) =>
        bench.Rec.Of(method).Last();

    static int LastSystemMessageId(WorldBench bench)
    {
        var call = Last(bench, FanoutSystemMessage);
        return Assert.IsType<SystemMessageMsg>(call[1]).Id;
    }

    const string FanoutWarehouseDepositList = "FanoutWarehouseDepositList";
    const string FanoutWarehouseWithdrawList = "FanoutWarehouseWithdrawList";
    const string FanoutSystemMessage = "FanoutSystemMessage";
}
