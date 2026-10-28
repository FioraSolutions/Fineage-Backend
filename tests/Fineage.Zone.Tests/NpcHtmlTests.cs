using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NpcHtmlTests
{
    const int MerchantObjectId = 61000;
    const int MerchantX = -70938;
    const int MerchantY = 258271;
    const int MerchantZ = -3104;

    [Fact]
    public void Substitution_replaces_the_window_tokens()
    {
        var html = NpcHtmlText.Apply(
            "<html>%objectId% %npcId% %npcname% %playername% %player_name% %festivalMins% %kept%</html>",
            new NpcHtmlText.Tokens(61000, 30001, "Lector", "Talker"));

        Assert.Equal("<html>61000 30001 Lector Talker Talker 0 %kept%</html>", html);
        Assert.Equal("", NpcHtmlText.Apply(null, new NpcHtmlText.Tokens(1, 2, "A", "B")));
    }

    [Fact]
    public void Range_is_150_in_three_dimensions()
    {
        Assert.Equal(150, NpcHtmlProtocol.InteractionDistance);
        Assert.Equal(QuestProtocol.TalkRange, NpcHtmlProtocol.InteractionDistance);
        Assert.True(NpcHtmlProtocol.InRange(0, 0, 0, 150, 0, 0));
        Assert.False(NpcHtmlProtocol.InRange(0, 0, 0, 151, 0, 0));
        Assert.True(NpcHtmlProtocol.InRange(0, 0, 0, 0, 0, -150));
        Assert.True(NpcHtmlProtocol.InRange(0, 0, 0, 100, 100, 50));
        Assert.False(NpcHtmlProtocol.InRange(0, 0, 0, 100, 100, 51));
    }

    [Fact]
    public void Paths_follow_the_npc_type()
    {
        Assert.Equal("merchant", NpcHtmlPaths.Folder("Merchant"));
        Assert.Equal("guard", NpcHtmlPaths.Folder("Guard"));
        Assert.Equal("villagemaster", NpcHtmlPaths.Folder("VillageMasterFighter"));
        Assert.Equal("default", NpcHtmlPaths.Folder("Folk"));
        Assert.Equal("30001.htm", NpcHtmlPaths.FileName(30001, 0));
        Assert.Equal("30001-2.htm", NpcHtmlPaths.FileName(30001, 2));

        var catalog = SampleCatalog();
        Assert.Contains("%objectId%", NpcHtmlPaths.Read(catalog, "Merchant", 30001, 0), StringComparison.Ordinal);
        Assert.Equal("<html><body>page-one</body></html>", NpcHtmlPaths.Read(catalog, "Merchant", 30001, 1));
        Assert.Equal(NpcHtmlText.NothingToSay, NpcHtmlPaths.Read(catalog, "Guard", 30040, 0));
        Assert.Equal(0, NpcHtmlPaths.ChatPage("Chat"));
        Assert.Equal(0, NpcHtmlPaths.ChatPage("Chat 0"));
        Assert.Equal(1, NpcHtmlPaths.ChatPage("Chat 1"));
        Assert.Equal(12, NpcHtmlPaths.ChatPage("Chat 12"));
    }

    [Fact]
    public void Bypass_parser_accepts_the_mobius_prefixes()
    {
        Assert.True(BypassCommands.TrySplit(61000, "Chat 1", out int id, out var command));
        Assert.Equal(61000, id);
        Assert.Equal("Chat 1", command);

        Assert.True(BypassCommands.TrySplit(0, "bypass -h npc_61000_Buy 1", out id, out command));
        Assert.Equal(61000, id);
        Assert.Equal("Buy 1", command);

        Assert.True(BypassCommands.TrySplit(61000, "npc_61000_Link common/crafting_01.htm", out id, out command));
        Assert.Equal("Link common/crafting_01.htm", command);

        Assert.False(BypassCommands.TrySplit(61000, "npc_60000_Buy 1", out _, out _));
        Assert.False(BypassCommands.TrySplit(0, "Chat 1", out _, out _));
        Assert.False(BypassCommands.TrySplit(61000, "npc_61000_", out _, out _));
        Assert.False(NpcHtmlPaths.TryRelative("Link ../items.json", out _));
        Assert.False(NpcHtmlPaths.TryRelative("Link /etc/passwd.htm", out _));
        Assert.True(NpcHtmlPaths.TryRelative("Link common/crafting_01.htm", out var relative));
        Assert.Equal("common/crafting_01.htm", relative);
    }

    [Fact]
    public void Registry_routes_chat_link_quest_and_stubs()
    {
        var catalog = SampleCatalog();
        var registry = BypassRegistry.CreateDefault(catalog);
        var call = new BypassCall
        {
            NpcObjectId = MerchantObjectId,
            NpcId = 30001,
            NpcName = "Lector",
            NpcType = "Merchant",
            PlayerName = "Talker",
            Command = ""
        };

        Assert.True(registry.Find("Chat 1")!.Handle(call with { Command = "Chat 1" }).Html!.Contains("page-one", StringComparison.Ordinal));
        Assert.True(registry.Find("Chat")!.Handle(call with { Command = "Chat" }).ReplayTalk);

        var link = registry.Find("Link common/crafting_01.htm")!.Handle(call with { Command = "Link common/crafting_01.htm" });
        Assert.Contains("craft", link.Html, StringComparison.Ordinal);
        Assert.Equal("bad_request", registry.Find("Link ../secret.htm")!.Handle(call with { Command = "Link ../secret.htm" }).Error);

        Assert.True(registry.Find("Quest")!.Handle(call with { Command = "Quest" }).ShowQuestList);
        Assert.True(registry.Find("Script")!.Handle(call with { Command = "Script" }).ShowQuestList);
        Assert.Contains(
            "not implemented yet",
            registry.Find("Script FirstClassTransferTalk")!.Handle(call with { Command = "Script FirstClassTransferTalk" }).Html,
            StringComparison.Ordinal);
        Assert.True(registry.Find("questlist")!.Handle(call with { Command = "questlist" }).ShowQuestList);
        var quest = registry.Find("Quest 999 accept")!.Handle(call with { Command = "Quest 999 accept" });
        Assert.True(quest.QuestAction);
        Assert.Equal(999, quest.QuestId);
        Assert.Equal("accept", quest.QuestEvent);
        var talk = registry.Find("quest_talk:999")!.Handle(call with { Command = "quest_talk:999" });
        Assert.True(talk.QuestAction);
        Assert.Equal(999, talk.QuestId);
        Assert.Equal("", talk.QuestEvent);

        Assert.Contains("Shop is not implemented yet.", registry.Find("Buy 1")!.Handle(call with { Command = "Buy 1" }).Html, StringComparison.Ordinal);
        Assert.Contains("Teleport is not implemented yet.", registry.Find("goto 15")!.Handle(call with { Command = "goto 15" }).Html, StringComparison.Ordinal);
        Assert.Contains("Teleport is not implemented yet.", registry.Find("showTeleports")!.Handle(call with { Command = "showTeleports" }).Html, StringComparison.Ordinal);
        Assert.Contains("Shop is not implemented yet.", registry.Find("exc_multisell 003")!.Handle(call with { Command = "exc_multisell 003" }).Html, StringComparison.Ordinal);
        Assert.Contains("Warehouse is not implemented yet.", registry.Find("DepositP")!.Handle(call with { Command = "DepositP" }).Html, StringComparison.Ordinal);
        Assert.Contains("Warehouse is not implemented yet.", registry.Find("package_deposit")!.Handle(call with { Command = "package_deposit" }).Html, StringComparison.Ordinal);
        Assert.Contains("Buff is not implemented yet.", registry.Find("SupportMagic")!.Handle(call with { Command = "SupportMagic" }).Html, StringComparison.Ordinal);
        Assert.Null(registry.Find("TerritoryStatus"));

        registry.Register(new ReplacingBuy());
        Assert.Equal("<html>custom</html>", registry.Find("Buy 3")!.Handle(call with { Command = "Buy 3" }).Html);
        Assert.Contains("Shop is not implemented yet.", registry.Find("Sell")!.Handle(call with { Command = "Sell" }).Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Imported_talking_island_merchant_page_is_present()
    {
        var catalog = NpcHtmlCatalog.LoadDefault();
        Assert.True(catalog.TryGet("merchant/30001.htm", out var html));
        Assert.Contains("Lector", html, StringComparison.Ordinal);
        Assert.Contains("%objectId%", html, StringComparison.Ordinal);
        Assert.True(catalog.Contains("npcdefault.htm"));
    }

    [Fact]
    public async Task Talk_and_bypass_use_range_html_and_stubs()
    {
        var catalog = SampleCatalog();
        var registry = BypassRegistry.CreateDefault(catalog);
        var server = new ZoneServer(new HandoffTokens(HandoffTokens.DefaultSecret), MerchantWorld(catalog, registry));
        var login = new LoginService(JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("talker", "pass1234", "Talker", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        await using var link = await Link.Open(server, npcPackets: 3);
        await link.EnterAsync(ticket.Value!);

        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = MerchantObjectId });
        var far = await link.ReadAsync<ErrorMsg>(MsgType.Error);
        Assert.Equal("range", far.Code);

        await link.SendAsync(MsgType.MoveTo, new MoveToMsg { DestX = MerchantX, DestY = MerchantY, DestZ = MerchantZ });
        await link.ReadAsync<MoveToMsg>(MsgType.MoveToAck);

        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = MerchantObjectId });
        var page = await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml);
        Assert.Equal(MerchantObjectId, page.NpcObjectId);
        Assert.Equal(30001, page.NpcId);
        Assert.Equal(0, page.ItemId);
        Assert.Contains("61000", page.Html, StringComparison.Ordinal);
        Assert.Contains("Lector", page.Html, StringComparison.Ordinal);
        Assert.Contains("Talker", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("%objectId%", page.Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Buy 1" });
        var shop = await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml);
        Assert.Contains("Lector", shop.Html, StringComparison.Ordinal);
        Assert.Contains("Shop is not implemented yet.", shop.Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg
        {
            NpcObjectId = 0,
            Command = $"bypass -h npc_{MerchantObjectId}_goto 15"
        });
        var gate = await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml);
        Assert.Contains("Teleport is not implemented yet.", gate.Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "DepositP" });
        Assert.Contains("Warehouse is not implemented yet.", (await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml)).Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "SupportMagic" });
        Assert.Contains("Buff is not implemented yet.", (await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml)).Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Chat 1" });
        Assert.Contains("page-one", (await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml)).Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg
        {
            NpcObjectId = MerchantObjectId,
            Command = "Link common/crafting_01.htm"
        });
        var craft = await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml);
        Assert.Contains("craft Lector", craft.Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Link ../secret.htm" });
        Assert.Equal("bad_request", (await link.ReadAsync<ErrorMsg>(MsgType.Error)).Code);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "Quest" });
        var list = await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        Assert.Equal("default", list.Hook);
        Assert.Equal(0, list.QuestId);
        Assert.Contains("I have nothing for you.", list.Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.BypassRequest, new BypassRequestMsg { NpcObjectId = MerchantObjectId, Command = "TerritoryStatus" });
        Assert.Contains("not implemented yet.", (await link.ReadAsync<NpcHtmlMsg>(MsgType.NpcHtml)).Html, StringComparison.Ordinal);
        await link.CloseAsync();
    }

    [Fact]
    public async Task Attackable_npc_does_not_open_html()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var server = new ZoneServer(tokens, new ZoneWorld());
        var login = new LoginService(JsonAccountStore.InMemory(), tokens);
        var ticket = login.Play("hunter", "pass1234", "Hunter", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        await using var link = await Link.Open(server, TalkingIsland.NpcCount);
        await link.EnterAsync(ticket.Value!);
        var gremlin = TalkingIsland.Gremlins[0];
        Assert.True(gremlin.Attackable);
        await link.SendAsync(MsgType.MoveTo, new MoveToMsg { DestX = gremlin.X, DestY = gremlin.Y, DestZ = gremlin.Z });
        await link.ReadAsync<MoveToMsg>(MsgType.MoveToAck);
        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = gremlin.ObjectId });
        var error = await link.ReadAsync<ErrorMsg>(MsgType.Error);
        Assert.Equal(NpcHtmlProtocol.Attackable, error.Code);
        await link.CloseAsync();
    }

    static NpcHtmlCatalog SampleCatalog() => NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
    {
        ["merchant/30001.htm"] = "<html><body>%npcname% %playername% %objectId% %npcId%</body></html>",
        ["merchant/30001-1.htm"] = "<html><body>page-one</body></html>",
        ["common/crafting_01.htm"] = "<html><body>craft %npcname%</body></html>"
    });

    static ZoneWorld MerchantWorld(NpcHtmlCatalog catalog, BypassRegistry registry)
    {
        var map = new SpawnMapDefinition(
            SpawnCatalog.DefaultMapId,
            true,
            [
                new SpawnNpcDefinition(
                    MerchantObjectId,
                    30001,
                    "Lector",
                    MerchantX,
                    MerchantY,
                    MerchantZ,
                    0,
                    false,
                    100,
                    80f,
                    40f,
                    60,
                    0,
                    "Trader",
                    "Merchant")
            ]);
        var spawns = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [SpawnCatalog.DefaultMapId] = map
        });
        return new ZoneWorld(spawnCatalog: spawns, html: catalog, bypasses: registry);
    }

    sealed class ReplacingBuy : IBypassHandler
    {
        public IReadOnlyList<string> Commands { get; } = ["Buy"];

        public BypassReply Handle(BypassCall call) => BypassReply.Page("<html>custom</html>");
    }

    sealed class Link : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly CancellationTokenSource _cts;
        readonly Task _server;
        readonly int _npcPackets;

        Link(TcpClient client, NetworkStream stream, CancellationTokenSource cts, Task server, int npcPackets)
        {
            _client = client;
            _stream = stream;
            _cts = cts;
            _server = server;
            _npcPackets = npcPackets;
        }

        public static async Task<Link> Open(ZoneServer server, int npcPackets)
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
            return new Link(client, client.GetStream(), cts, accept, npcPackets);
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
            for (int i = 0; i < _npcPackets; i++)
                await ReadAsync<NpcInfoMsg>(MsgType.NpcInfo);
            var welcome = await ReadAsync<SystemMessageMsg>(MsgType.SystemMessage);
            Assert.Equal(ChatProtocol.Welcome, welcome.Id);
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
