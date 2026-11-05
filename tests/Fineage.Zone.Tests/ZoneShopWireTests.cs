using System.Net;
using System.Net.Sockets;
using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneShopWireTests
{
    const int MerchantObjectId = 61000;
    const int MerchantX = -71338;
    const int MerchantY = 258271;
    const int MerchantZ = -3104;

    [Fact]
    public async Task Buy_sell_and_multisell_move_adena_and_send_inventory_updates()
    {
        var clock = new MutableClock();
        await using var link = await Open(ShopWorld(clock), "buyer");
        await link.MoveToMerchantAsync();

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Buy 1" });
        var list = await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        Assert.Equal(1, list.ListId);
        Assert.Equal(40, list.Items.Single(item => item.ItemId == 200).Price);
        Assert.Equal(-1, list.Items.Single(item => item.ItemId == 200).Count);
        Assert.Equal(0, list.CastleTaxPercent);
        var balance = await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);
        Assert.Equal(1000, balance.Adena);

        await link.SendAsync(MsgType.RequestBuyItem, new RequestBuyItemMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 1,
            Items = [new ShopCountMsg { ItemId = 200, Count = 2 }]
        });
        var bought = await link.ReadAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        Assert.Contains(bought.Changes, row => row.ItemId == 57 && row.Count == 920);
        Assert.Contains(bought.Changes, row => row.ItemId == 200 && row.Count == 2 && row.Change == InventoryChange.Add);
        Assert.Equal(ShopProtocol.TransactionComplete, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        Assert.Equal(920, (await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance)).Adena);

        await link.SendAsync(MsgType.RequestBuyItem, new RequestBuyItemMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 1,
            Items = [new ShopCountMsg { ItemId = 200, Count = 100 }]
        });
        Assert.Equal(ShopProtocol.NotEnoughAdena, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Sell" });
        var sell = await link.ReadAsync<SellListMsg>(MsgType.SellList);
        var sword = Assert.Single(sell.Items, item => item.ItemId == 100);
        Assert.Equal(500, sword.Price);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);

        await link.SendAsync(MsgType.RequestSellItem, new RequestSellItemMsg
        {
            NpcObjectId = MerchantObjectId,
            Items = [new ShopObjectCountMsg { ObjectId = sword.ObjectId, Count = 1 }]
        });
        var sold = await link.ReadAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        Assert.Contains(sold.Changes, row => row.ObjectId == sword.ObjectId && row.Change == InventoryChange.Remove);
        Assert.Contains(sold.Changes, row => row.ItemId == 57 && row.Count == 1420);
        Assert.Equal(ShopProtocol.TransactionComplete, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await link.ReadAsync<SellListMsg>(MsgType.SellList);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Multisell 2" });
        var multi = await link.ReadAsync<MultiSellListMsg>(MsgType.MultiSellList);
        Assert.False(multi.InventoryOnly);
        Assert.Equal(200_000, multi.Entries[0].EntryId);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);
        await link.SendAsync(MsgType.RequestMultiSellChoose, new RequestMultiSellChooseMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 2,
            EntryId = multi.Entries[0].EntryId,
            Amount = 1
        });
        var exchanged = await link.ReadAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        Assert.Contains(exchanged.Changes, row => row.ItemId == 500 && row.EnchantLevel == 0 && row.Change == InventoryChange.Add);
        Assert.Equal(ShopProtocol.TransactionComplete, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(1320, (await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance)).Adena);
        await link.CloseAsync();
    }

    [Fact]
    public async Task Weight_slots_and_limited_stock_refuse_without_taking_adena()
    {
        var clock = new MutableClock();
        await using var link = await Open(LimitWorld(clock), "limits");
        await link.MoveToMerchantAsync();

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Buy 7" });
        var list = await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        Assert.Equal(1, list.Items.Single(item => item.ItemId == 300).Count);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);

        await link.SendAsync(MsgType.RequestBuyItem, new RequestBuyItemMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 7,
            Items = [new ShopCountMsg { ItemId = 400, Count = 1 }]
        });
        Assert.Equal(ShopProtocol.WeightLimit, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);

        await link.SendAsync(MsgType.RequestBuyItem, new RequestBuyItemMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 7,
            Items = [new ShopCountMsg { ItemId = 300, Count = 1 }]
        });
        var bought = await link.ReadAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        Assert.Contains(bought.Changes, row => row.ItemId == 300 && row.Count == 1);
        Assert.Equal(ShopProtocol.TransactionComplete, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        var after = await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        Assert.Equal(0, after.Items.Single(item => item.ItemId == 300).Count);
        Assert.Equal(950, (await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance)).Adena);

        await link.SendAsync(MsgType.RequestBuyItem, new RequestBuyItemMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 7,
            Items = [new ShopCountMsg { ItemId = 300, Count = 1 }]
        });
        Assert.Equal(ShopProtocol.OutOfStock, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);

        clock.Now = clock.Now.AddMinutes(2);
        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Buy 7" });
        var restocked = await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        Assert.Equal(1, restocked.Items.Single(item => item.ItemId == 300).Count);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);
        await link.CloseAsync();
    }

    [Fact]
    public async Task Giran_buy_list_shows_base_tax_and_owner_tax()
    {
        var world = ShopWorld(new MutableClock(), "giran");
        var castle = world.FindCastle(3);
        Assert.NotNull(castle);
        await using var link = await Open(world, "taxed");
        await link.MoveToMerchantAsync();
        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Buy 1" });
        var untaxedOwner = await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        Assert.Equal(10, untaxedOwner.BaseTaxPercent);
        Assert.Equal(0, untaxedOwner.CastleTaxPercent);
        Assert.Equal(44, untaxedOwner.Items.Single(item => item.ItemId == 200).Price);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);

        castle!.OwnerClanId = 8;
        castle.TaxPercent = 10;
        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Buy 1" });
        var taxed = await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        Assert.Equal(10, taxed.CastleTaxPercent);
        Assert.Equal(48, taxed.Items.Single(item => item.ItemId == 200).Price);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);
        await link.CloseAsync();
    }

    static ZoneWorld ShopWorld(MutableClock clock, string mapId = "shop_flat")
    {
        var items = Catalog();
        var game = GameData.From(items, new StarterKits(
        [
            new StarterKit(HumanClasses.Fighter,
            [
                new StarterKitEntry(57, 1000, false),
                new StarterKitEntry(100, 1, false)
            ])
        ]));
        return World(game, clock, mapId, BuyLists(), MultiLists());
    }

    [Fact]
    public async Task A_full_bag_refuses_another_slot()
    {
        var entries = new List<StarterKitEntry> { new(57, 1000, false) };
        for (int i = 0; i < 79; i++)
            entries.Add(new StarterKitEntry(100, 1, false));
        var game = GameData.From(Catalog(), new StarterKits([new StarterKit(HumanClasses.Fighter, entries)]));
        await using var link = await Open(World(game, new MutableClock(), "shop_flat", BuyLists(), MultiLists()), "full");
        await link.MoveToMerchantAsync();
        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Buy 1" });
        await link.ReadAsync<BuyListMsg>(MsgType.BuyList);
        await link.ReadAsync<ShopBalanceMsg>(MsgType.ShopBalance);
        await link.SendAsync(MsgType.RequestBuyItem, new RequestBuyItemMsg
        {
            NpcObjectId = MerchantObjectId,
            ListId = 1,
            Items = [new ShopCountMsg { ItemId = 200, Count = 1 }]
        });
        Assert.Equal(ShopProtocol.InventoryFull, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        await link.CloseAsync();
    }

    static ZoneWorld LimitWorld(MutableClock clock)
    {
        var items = Catalog();
        var game = GameData.From(items, new StarterKits(
        [
            new StarterKit(HumanClasses.Fighter, [new StarterKitEntry(57, 1000, false)])
        ]));
        var buys = BuyListCatalog.From(
        [
            new BuyListDefinition(7, [30001],
            [
                new BuyProduct(200, 40),
                new BuyProduct(300, 50, maxCount: 1, restockMinutes: 1),
                new BuyProduct(400, 10)
            ])
        ]);
        return World(game, clock, "shop_flat", buys, MultiSellCatalog.From(
        [
            new MultiSellListDefinition(2, false, false, [30001],
            [
                new MultiSellEntry(1, [new MultiSellPart(57, 1)], [new MultiSellPart(500, 1)])
            ])
        ]));
    }

    static ZoneWorld World(GameData game, MutableClock clock, string mapId, BuyListCatalog buys, MultiSellCatalog multis)
    {
        var map = new SpawnMapDefinition(mapId, true,
        [
            new SpawnNpcDefinition(MerchantObjectId, 30001, "Lector", MerchantX, MerchantY, MerchantZ, 0, false, 100, 80f, 40f, 60, 0, "Trader", "Merchant")
        ]);
        var spawns = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [mapId] = map });
        return new ZoneWorld(
            game: game,
            spawnCatalog: spawns,
            activeMapId: mapId,
            clock: () => clock.Now,
            buyLists: buys,
            multisells: multis);
    }

    static BuyListCatalog BuyLists() => BuyListCatalog.From(
    [
        new BuyListDefinition(1, [30001], [new BuyProduct(200, 40), new BuyProduct(500, 100)])
    ]);

    static MultiSellCatalog MultiLists() => MultiSellCatalog.From(
    [
        new MultiSellListDefinition(2, false, false, [30001],
        [
            new MultiSellEntry(2, [new MultiSellPart(200, 1), new MultiSellPart(57, 100)], [new MultiSellPart(500, 1)])
        ])
    ]);

    static ItemCatalog Catalog() => new(
    [
        new ItemTemplate(57, "Adena", ItemKind.Etc, BodySlot.None, "icon.adena", Stackable: true, ReferencePrice: 1, Sellable: false),
        new ItemTemplate(100, "Sword", ItemKind.Weapon, BodySlot.RightHand, "icon.sword", WeaponType.Sword, ReferencePrice: 1000, Weight: 100),
        new ItemTemplate(200, "Potion", ItemKind.Etc, BodySlot.None, "icon.potion", Stackable: true, ReferencePrice: 40, Weight: 1),
        new ItemTemplate(300, "Stone", ItemKind.Etc, BodySlot.None, "icon.stone", Stackable: true, ReferencePrice: 50, Weight: 1),
        new ItemTemplate(400, "Anvil", ItemKind.Etc, BodySlot.None, "icon.anvil", ReferencePrice: 10, Weight: 5_000_000),
        new ItemTemplate(500, "Blade", ItemKind.Weapon, BodySlot.RightHand, "icon.blade", WeaponType.Sword, ReferencePrice: 2000, Weight: 100)
    ]);

    sealed class MutableClock
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    }

    static async Task<Link> Open(ZoneWorld world, string name)
    {
        var server = new ZoneServer(new HandoffTokens(HandoffTokens.DefaultSecret), world);
        var login = new LoginService(JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play(name, "pass1234", name, HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        var link = await Link.Open(server);
        await link.EnterAsync(ticket.Value!);
        return link;
    }

    sealed class Link : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly CancellationTokenSource _cts;
        readonly Task _server;

        Link(TcpClient client, NetworkStream stream, CancellationTokenSource cts, Task server)
        {
            _client = client;
            _stream = stream;
            _cts = cts;
            _server = server;
        }

        public static async Task<Link> Open(ZoneServer server)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var cts = new CancellationTokenSource();
            var accept = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                listener.Stop();
                await using var stream = accepted.GetStream();
                await server.HandleAsync(stream, cts.Token);
            });
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new Link(client, client.GetStream(), cts, accept);
        }

        public async Task EnterAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg());
            await ReadAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            await ReadAsync<AuthOkMsg>(MsgType.AuthOk);
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            await ReadAsync<UserInfoMsg>(MsgType.UserInfo);
            await ReadAsync<ItemListMsg>(MsgType.ItemList);
            await ReadAsync<SkillListMsg>(MsgType.SkillList);
            await ReadAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < 3; i++)
                await ReadAsync<NpcInfoMsg>(MsgType.NpcInfo);
            Assert.Equal(ChatProtocol.Welcome, (await ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        }

        public async Task MoveToMerchantAsync()
        {
            await SendAsync(MsgType.MoveTo, new MoveToMsg { DestX = MerchantX, DestY = MerchantY, DestZ = MerchantZ });
            await ReadAsync<MoveToMsg>(MsgType.MoveToAck);
        }

        public async Task SendAsync<T>(MsgType type, T message) =>
            await FrameIO.SendAsync(_stream, (ushort)type, MessagePackSerializer.Serialize(message));

        public async Task<T> ReadAsync<T>(MsgType type)
        {
            var frame = await FrameIO.ReadAsync(_stream);
            Assert.NotNull(frame);
            Assert.Equal((ushort)type, frame!.Value.Type);
            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public async Task CloseAsync()
        {
            _client.Close();
            await _server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _cts.CancelAsync();
            try
            {
                await _server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
