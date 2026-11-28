using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Talking to an NPC and following a bypass link: the page the client receives, the range gate, the refusals, and
/// the merchant stubs. Ported off the V2 wire; the world is driven through <see cref="WorldBench"/> and the pages
/// read from the recorded <see cref="Fineage.Zone.World.IWorldEventPublisher"/> fanout.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NpcHtmlTests
{
    const int MerchantObjectId = 61000;
    const int MerchantNpcId = 30001;

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
    public void Range_is_250_in_three_dimensions()
    {
        Assert.Equal(250, NpcHtmlProtocol.InteractionDistance);
        Assert.Equal(QuestProtocol.TalkRange, NpcHtmlProtocol.InteractionDistance);
        Assert.True(NpcHtmlProtocol.InRange(0, 0, 0, 250, 0, 0));
        Assert.False(NpcHtmlProtocol.InRange(0, 0, 0, 251, 0, 0));
        Assert.True(NpcHtmlProtocol.InRange(0, 0, 0, 0, 0, -250));
        Assert.True(NpcHtmlProtocol.InRange(0, 0, 0, 100, 100, 50));
        Assert.False(NpcHtmlProtocol.InRange(0, 0, 0, 200, 200, 0));
    }

    [Fact]
    public void Paths_follow_the_npc_type()
    {
        Assert.Equal("merchant", NpcHtmlPaths.Folder("Merchant"));
        Assert.Equal("guard", NpcHtmlPaths.Folder("Guard"));
        Assert.Equal("villagemaster", NpcHtmlPaths.Folder("VillageMasterFighter"));
        Assert.Equal("default", NpcHtmlPaths.Folder("Folk"));
        // Mobius DungeonGatekeeper/SepulcherNpc override getHtmlPath; routing them to default/ served
        // the "nothing to say" page to every Necropolis/Catacomb ziggurat and every tomb NPC.
        Assert.Equal("teleporter", NpcHtmlPaths.Folder("DungeonGatekeeper"));
        Assert.Equal("SepulcherNpc", NpcHtmlPaths.Folder("SepulcherNpc"));
        // CT 0: Four Sepulchers NPCs are Folk; the FourSepulchers script serves {id}.html.
        Assert.Equal("SepulcherNpc", NpcHtmlPaths.Folder("Folk", 31921));
        Assert.Equal("SepulcherNpc", NpcHtmlPaths.Folder("Folk", 31452));
        Assert.Equal("default", NpcHtmlPaths.Folder("Folk", 31147));
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
            NpcId = MerchantNpcId,
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
        // Named Mobius scripts go to the world (village masters); unknown ones fall back to the Unavailable page there.
        var script = registry.Find("Quest FirstClassTransferTalk 30154_mystic.html")!.Handle(call with { Command = "Quest FirstClassTransferTalk 30154_mystic.html" });
        Assert.Equal("FirstClassTransferTalk", script.ScriptName);
        Assert.Equal("30154_mystic.html", script.ScriptEvent);
        Assert.Equal("", registry.Find("Script FirstClassTransferTalk")!.Handle(call with { Command = "Script FirstClassTransferTalk" }).ScriptEvent);
        Assert.True(registry.Find("questlist")!.Handle(call with { Command = "questlist" }).ShowQuestList);
        var quest = registry.Find("Quest 999 accept")!.Handle(call with { Command = "Quest 999 accept" });
        Assert.True(quest.QuestAction);
        Assert.Equal(999, quest.QuestId);
        Assert.Equal("accept", quest.QuestEvent);
        var talk = registry.Find("quest_talk:999")!.Handle(call with { Command = "quest_talk:999" });
        Assert.True(talk.QuestAction);
        Assert.Equal(999, talk.QuestId);
        Assert.Equal("", talk.QuestEvent);

        Assert.Equal(1, registry.Find("Buy 1")!.Handle(call with { Command = "Buy 1" }).BuyListId);
        Assert.Equal(NpcServiceIntent.Teleport, registry.Find("goto 15")!.Handle(call with { Command = "goto 15" }).Service);
        Assert.Equal("NORMAL", registry.Find("goto 15")!.Handle(call with { Command = "goto 15" }).ServiceArg);
        Assert.Equal(15, registry.Find("goto 15")!.Handle(call with { Command = "goto 15" }).ServiceId);
        Assert.Equal(NpcServiceIntent.TeleportList, registry.Find("showTeleports")!.Handle(call with { Command = "showTeleports" }).Service);
        Assert.Equal(NpcServiceIntent.Teleport, registry.Find("teleport OTHER 0")!.Handle(call with { Command = "teleport OTHER 0" }).Service);
        Assert.Equal("OTHER", registry.Find("teleport OTHER 0")!.Handle(call with { Command = "teleport OTHER 0" }).ServiceArg);
        Assert.Equal(0, registry.Find("teleport OTHER 0")!.Handle(call with { Command = "teleport OTHER 0" }).ServiceId);
        Assert.Equal(NpcServiceIntent.NoblesSelect, registry.Find("showNoblesSelect")!.Handle(call with { Command = "showNoblesSelect" }).Service);
        Assert.Equal(NpcServiceIntent.RequestNobless, registry.Find("RequestNobless")!.Handle(call with { Command = "RequestNobless" }).Service);
        var exchange = registry.Find("exc_multisell 003")!.Handle(call with { Command = "exc_multisell 003" });
        Assert.Equal(3, exchange.MultiSellListId);
        Assert.True(exchange.MultiSellInventoryOnly);
        Assert.Equal(3000100, registry.Find("Wear 3000100")!.Handle(call with { Command = "Wear 3000100" }).WearListId);
        Assert.Equal(NpcServiceIntent.Deposit, registry.Find("DepositP")!.Handle(call with { Command = "DepositP" }).Service);
        Assert.Equal(NpcServiceIntent.Withdraw, registry.Find("WithdrawP")!.Handle(call with { Command = "WithdrawP" }).Service);
        Assert.Equal(NpcServiceIntent.PackageDeposit, registry.Find("package_deposit")!.Handle(call with { Command = "package_deposit" }).Service);
        Assert.Equal(NpcServiceIntent.PackageWithdraw, registry.Find("package_withdraw")!.Handle(call with { Command = "package_withdraw" }).Service);
        Assert.Equal(NpcServiceIntent.SupportMagic, registry.Find("SupportMagic")!.Handle(call with { Command = "SupportMagic" }).Service);
        Assert.Equal(NpcServiceIntent.GiveBlessing, registry.Find("GiveBlessing")!.Handle(call with { Command = "GiveBlessing" }).Service);
        Assert.IsType<TerritoryBypassHandler>(registry.Find("TerritoryStatus"));

        registry.Register(new ReplacingBuy());
        Assert.Equal("<html>custom</html>", registry.Find("Buy 3")!.Handle(call with { Command = "Buy 3" }).Html);
        Assert.True(registry.Find("Sell")!.Handle(call with { Command = "Sell" }).OpenSell);
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
    public async Task Talk_renders_the_page_and_bypass_follows_the_links()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("talker", "Talker");

        Assert.Null(await actor.Talk(MerchantObjectId));
        var page = Assert.IsType<NpcHtmlMsg>(Bench(bench, FanoutHtml).Last()[1]);
        Assert.Equal(MerchantObjectId, page.NpcObjectId);
        Assert.Equal(MerchantNpcId, page.NpcId);
        Assert.Equal(0, page.ItemId);
        // Tokens are substituted before the page leaves the world; the raw template markers never reach a client.
        Assert.Contains(MerchantObjectId.ToString(), page.Html, StringComparison.Ordinal);
        Assert.Contains("Lector", page.Html, StringComparison.Ordinal);
        Assert.Contains("Talker", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("%objectId%", page.Html, StringComparison.Ordinal);

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Buy 1"));
        Assert.Contains("I have no such list.", LastHtml(bench), StringComparison.Ordinal);

        // Anonymous bypass (no target NPC) still resolves the id out of the Mobius prefix.
        Assert.Null(await actor.BypassAsync(0, $"bypass -h npc_{MerchantObjectId}_goto 15"));
        var gateNotice = Assert.IsType<SystemMessageMsg>(Bench(bench, FanoutSystemMessage).Last()[1]);
        Assert.Equal(NpcServiceProtocol.UnknownTeleport, gateNotice.Id);
        Assert.Contains("not available", LastHtml(bench), StringComparison.Ordinal);

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "DepositP"));
        Assert.Contains("warehouse keeper", LastHtml(bench), StringComparison.OrdinalIgnoreCase);

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "SupportMagic"));
        var buffNotice = Assert.IsType<SystemMessageMsg>(Bench(bench, FanoutSystemMessage).Last()[1]);
        Assert.Equal(NpcServiceProtocol.BuffLevelLow, buffNotice.Id);
        Assert.Contains("low level", LastHtml(bench), StringComparison.OrdinalIgnoreCase);

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Chat 1"));
        Assert.Contains("page-one", LastHtml(bench), StringComparison.Ordinal);

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Link common/crafting_01.htm"));
        Assert.Contains("craft Lector", LastHtml(bench), StringComparison.Ordinal);

        // A relative link may not escape the html folder.
        Assert.Equal("bad_request", await actor.BypassAsync(MerchantObjectId, "Link ../secret.htm"));

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Quest"));
        // Lector 30001 is start NPC for Q258 Bring Wolf Pelts — Quest opens the gated list, not the empty stub.
        Assert.Contains("quest_talk:258", LastHtml(bench), StringComparison.Ordinal);
        Assert.Contains("Bring Wolf Pelts", LastHtml(bench), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Territory_status_opens_the_live_castle_page()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("talker", "Talker");

        Assert.Null(await actor.BypassAsync(MerchantObjectId, "TerritoryStatus"));
        // The merchant stands in tax zone oren_2119_001 (domain 4). TerritoryStatus names that castle.
        Assert.Contains("Oren", LastHtml(bench), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Talk_out_of_range_is_refused()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("talker", "Talker");
        // Well outside the 150 unit talk radius without moving the pawn through the world.
        actor.MoveTo(Spawns.HumanStartX + 1_000, Spawns.HumanStartY, Spawns.HumanStartZ);

        // Mobius NpcAction: out of canInteract starts AI_INTENTION_INTERACT instead of refusing.
        Assert.Null(await actor.Talk(MerchantObjectId));
        Assert.Equal(MerchantObjectId, actor.Player.Intent.PendingTalkNpcId);
        // The walk aims at moveToPawn(60) + both collision radii, not at the 250 talk radius minus the old 20 slack:
        // aiming the walk at the gate left the pawn just outside the 3D sphere it had to reach.
        int npcX = actor.Player.MoveDestX, npcY = actor.Player.MoveDestY;
        int reach = ZoneCombat.Reach(ZoneCombat.InteractOffset, ZoneCombat.PlayerCollisionRadius, ZoneCombat.DefaultNpcCollision);
        Assert.True(ZoneCombat.Within(npcX, npcY, Spawns.HumanStartX, Spawns.HumanStartY, reach + 1),
            $"the approach must stop at the interaction reach ({reach}), not at the talk gate");
        Assert.Equal("range", await actor.BypassAsync(MerchantObjectId, "Buy 1"));
        Assert.Empty(Bench(bench, FanoutHtml));
    }

    /// <summary>
    /// The Elven Village gatekeeper case from <c>bots-200-town-soak</c>: the NPC stands on a platform 464 units above the
    /// ground the pawn can walk on, so a flat 230-unit stop sits outside the 3D 250 sphere and every bypass answered
    /// "range" forever. Mobius <c>moveToLocation</c> folds <c>|dz|</c> into the offset, so the pawn keeps walking under the
    /// target and climbs the ramp (<c>Creature.java:4346-4368</c>).
    /// </summary>
    [Fact]
    public void Elevated_npc_keeps_the_walk_going_until_the_3d_gate_is_inside()
    {
        const int npcHeight = 464;
        int reach = ZoneCombat.Reach(ZoneCombat.InteractOffset, ZoneCombat.PlayerCollisionRadius, ZoneCombat.DefaultNpcCollision);
        double level = ZoneWorld.ApproachStopDistance(reach, 0);
        double raised = ZoneWorld.ApproachStopDistance(reach, npcHeight);

        // A pawn standing level stops ~60 + collisions away — the same number Mobius lands on.
        Assert.Equal(reach - ZoneCombat.ApproachSlack, level);
        // Under a raised NPC the height comes out of the offset, so the stop collapses to the minimum: walk in, climb.
        Assert.Equal(10, raised);
        Assert.True(raised < level, "a raised target must pull the pawn closer, not hold it back");

        // And the number that mattered in the soak: 230 XY units under a 464-high NPC is outside the talk sphere.
        Assert.False(NpcHtmlProtocol.InRange(0, 0, 0, 230, 0, npcHeight));
        // Walking into the NPC's column is what makes the climb possible at all: the geo path then takes the ramp and the
        // arrival matches what the stop promised. Standing at the stop on the NPC's own ground layer is inside the sphere.
        int stop = (int)raised;
        Assert.True(NpcHtmlProtocol.InRange(0, 0, npcHeight, stop, 0, npcHeight));
    }

    /// <summary>Mobius <c>Npc.canInteract</c> is a 3D sphere; the warehouse/quest-event packet gates are 2D circles.</summary>
    [Fact]
    public void Retail_gates_split_into_the_3d_sphere_and_the_2d_circle()
    {
        const int x = 0, y = 0, z = 0;
        // 240 across and 200 up: 312 apart in space, 240 along the ground.
        Assert.False(NpcHtmlProtocol.InRange(x, y, z, 240, 0, 200));
        Assert.True(ZoneCombat.Within(x, y, 240, 0, NpcHtmlProtocol.InteractionDistance));
        Assert.False(ZoneCombat.Within(x, y, 251, 0, NpcHtmlProtocol.InteractionDistance));
    }

    /// <summary>
    /// Mobius <c>RequestBypassToServer</c> validates the <c>npc_</c> branch with <c>isInsideRadius2D</c>, while
    /// <c>Npc.canInteract</c> (talk) is <c>isInsideRadius3D</c>. Fineage answered both with the sphere, so a pawn on the
    /// ground below a gatekeeper on a platform was inside 250 along the ground and still got "range" on every link —
    /// the bot soak reached a gatekeeper dialog but could never follow it to a teleport.
    /// </summary>
    [Fact]
    public async Task Bypass_link_uses_the_flat_circle_while_talk_uses_the_sphere()
    {
        const int x = 0, y = 0, z = 0;
        // 240 along the ground, 200 up: inside the flat circle, outside the sphere.
        Assert.True(ZoneCombat.Within(x, y, 240, 0, NpcHtmlProtocol.InteractionDistance));
        Assert.False(NpcHtmlProtocol.InRange(x, y, z, 240, 0, 200));

        var catalog = SampleCatalog();
        var bench = new WorldBench(
            spawns: MerchantAt(Spawns.HumanStartX, Spawns.HumanStartY, Spawns.HumanStartZ + 200),
            html: catalog,
            bypasses: BypassRegistry.CreateDefault(catalog));
        BenchPlayer actor = await bench.EnterAsync("browser", "Browser",
            x: Spawns.HumanStartX + 240, y: Spawns.HumanStartY, z: Spawns.HumanStartZ);

        // Talk still needs the sphere: the pawn is 200 up from the merchant.
        Assert.Null(await actor.Talk(MerchantObjectId));
        Assert.Equal(MerchantObjectId, actor.Player.Intent.PendingTalkNpcId);

        // The link is gated by the flat circle, so the same position opens the page the click asked for.
        Assert.Null(await actor.BypassAsync(MerchantObjectId, "Buy 1"));
        Assert.NotEmpty(Bench(bench, FanoutHtml));
    }

    [Fact]
    public async Task Attackable_npc_does_not_open_html()
    {
        var bench = new WorldBench(spawns: SpawnCatalog.LoadDefault());
        var gremlin = TalkingIsland.Gremlins[0];
        BenchPlayer actor = await bench.EnterAsync(
            "hunter", "Hunter", x: gremlin.X, y: gremlin.Y, z: gremlin.Z);

        Assert.True(gremlin.Attackable);
        Assert.Equal(NpcHtmlProtocol.Attackable, await actor.Talk(gremlin.ObjectId));
        Assert.Empty(Bench(bench, FanoutHtml));
    }

    /// <summary>A world whose only NPC is the sample merchant, standing where the bench puts a human.</summary>
    static WorldBench Open()
    {
        var catalog = SampleCatalog();
        return new WorldBench(
            spawns: MerchantAt(Spawns.HumanStartX, Spawns.HumanStartY, Spawns.HumanStartZ),
            html: catalog,
            bypasses: BypassRegistry.CreateDefault(catalog));
    }

    static List<object?[]> Bench(WorldBench bench, string method) => bench.Rec.Of(method);

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(Bench(bench, FanoutHtml).Last()[1]).Html;

    // The recorder is keyed by interface method name, so spell them once instead of scattering strings.
    const string FanoutHtml = "FanoutNpcHtml";
    const string FanoutSystemMessage = "FanoutSystemMessage";

    static NpcHtmlCatalog SampleCatalog() => NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
    {
        ["merchant/30001.htm"] = "<html><body>%npcname% %playername% %objectId% %npcId%</body></html>",
        ["merchant/30001-1.htm"] = "<html><body>page-one</body></html>",
        ["common/crafting_01.htm"] = "<html><body>craft %npcname%</body></html>"
    });

    static SpawnCatalog MerchantAt(int x, int y, int z)
    {
        var npc = new SpawnNpcDefinition(
            MerchantObjectId, MerchantNpcId, "Lector", x, y, z, 0, false, 100, 80f, 40f, 60, 0, "Trader", "Merchant");
        var map = new SpawnMapDefinition(SpawnCatalog.DefaultMapId, true, [npc]);
        return SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [SpawnCatalog.DefaultMapId] = map });
    }

    sealed class ReplacingBuy : IBypassHandler
    {
        public IReadOnlyList<string> Commands { get; } = ["Buy"];

        public BypassReply Handle(BypassCall call) => BypassReply.Page("<html>custom</html>");
    }
}
