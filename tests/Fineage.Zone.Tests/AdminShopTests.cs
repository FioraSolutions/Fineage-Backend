using Fineage.Accounts;
using Fineage.Game.Shops;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.Combat;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Tests that swap <see cref="ZoneAdmin"/> and <c>FINEAGE_ZONE_ADMIN</c>. Both are process-wide, and
/// <c>data/admin-characters.json</c> is <c>*</c>, so a parallel dispose reloads "any name" and a non-admin passes.
/// </summary>
[CollectionDefinition("Zone admin gate", DisableParallelization = true)]
public sealed class ZoneAdminGateCollection;

/// <summary>Mobius admin_gmshop / admin_buy / setlevel gated by KuK + FINEAGE_ZONE_ADMIN.</summary>
[Collection("Zone admin gate")]
public sealed class AdminShopTests : IDisposable
{
    readonly string? _prevEnv;

    public AdminShopTests()
    {
        _prevEnv = Environment.GetEnvironmentVariable(ZoneAdmin.EnvVar);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
        ZoneAdmin.SetNamesForTest(["KuK"]);
    }

    public void Dispose()
    {
        ZoneAdmin.SetNamesForTest(null);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, _prevEnv);
    }

    [Fact]
    public void Allowlist_requires_env_and_name()
    {
        Assert.True(ZoneAdmin.IsAdminCharacter("KuK"));
        Assert.True(ZoneAdmin.IsAdminCharacter("kuk"));
        Assert.False(ZoneAdmin.IsAdminCharacter("Other"));

        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "0");
        Assert.False(ZoneAdmin.IsAdminCharacter("KuK"));
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");

        ZoneAdmin.SetNamesForTest([ZoneAdmin.AnyCharacter]);
        Assert.True(ZoneAdmin.IsAdminCharacter("Other"));
        Assert.True(ZoneAdmin.IsAdminCharacter("anyone"));
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "0");
        Assert.False(ZoneAdmin.IsAdminCharacter("anyone"));
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
        ZoneAdmin.SetNamesForTest(["KuK"]);
    }

    [Fact]
    public async Task Gmshop_html_and_boss_jewels_buy_for_KuK()
    {
        var buyLists = BuyListCatalog.From(
        [
            new BuyListDefinition(9917, [],
            [
                new BuyProduct(6658, 0), // Ring of Baium
                new BuyProduct(6659, 0)  // Zaken's Earring
            ])
        ]);
        var html = NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
        {
            ["admin/gmshops.htm"] = "<html><title>Shop</title><body>GM</body></html>",
            ["admin/gmstore/gear/jewels.htm"] = "<html><body>Jewels</body></html>"
        });

        var kit = new WorldBench(buyLists: buyLists, html: html);
        BenchPlayer me = await kit.EnterAsync(name: "KuK");

        Assert.Null(await me.BypassAsync(0, "admin_gmshop"));
        var page = Assert.IsType<NpcHtmlMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutNpcHtml)).Last()[1]);
        Assert.Contains("GM", page.Html, StringComparison.Ordinal);

        Assert.Null(await me.BypassAsync(0, "admin_buy 9917"));
        var buy = Assert.IsType<Fineage.Protocol.BuyListMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutBuyList)).Last()[1]);
        Assert.Equal(9917, buy.ListId);
        Assert.Equal(0, buy.NpcObjectId);

        Assert.Null(await me.Buy(0, 9917, (6658, 1)));
        Assert.True(me.Inventory.CountOf(6658) >= 1);
    }

    [Fact]
    public async Task Currency_page_grants_full_amount_of_every_row()
    {
        // Live catalog: GM Shop "Currency" is buylist 9916 (Adena, Ancient Adena, Festival Adena,
        // Gold Bar, Apiga, Golden Apiga) with price 0 on every row.
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");

        foreach (int itemId in new[] { 57, 5575, 6673, 3470, 9142, 9143 })
            Assert.True(kit.Game.Items.TryGet(itemId, out _), "currency item " + itemId + " missing from items.json");

        Assert.Null(await me.BypassAsync(0, "admin_buy 9916"));

        // Above MaxGrantCount (10 000): the admin page is used to mint large sums, the cap that
        // guards admin_create_item must not truncate or refuse the purchase.
        Assert.Null(await me.Buy(0, 9916, (57, 25_000)));
        Assert.Equal(25_000, me.Inventory.CountOf(57));

        Assert.Null(await me.Buy(0, 9916, (3470, 10)));
        Assert.Equal(10, me.Inventory.CountOf(3470));

        // Free rows stay in the list (infinite GM stock, never sold out).
        var buy = Assert.IsType<Fineage.Protocol.BuyListMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutBuyList)).Last()[1]);
        Assert.All(buy.Items, row => Assert.Equal(-1, row.Count));
    }

    [Fact]
    public async Task Non_admin_name_is_forbidden()
    {
        var kit = new WorldBench(html: NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
        {
            ["admin/gmshops.htm"] = "<html><body>GM</body></html>"
        }));
        BenchPlayer me = await kit.EnterAsync(name: "NotAdmin");
        Assert.Equal("forbidden", await me.BypassAsync(0, "admin_gmshop"));
    }

    [Fact]
    public async Task Setlevel_and_setclass_update_player()
    {
        var kit = new WorldBench(html: NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
        {
            ["admin/setclass/human_fighter.htm"] = "<html><body>Classes</body></html>"
        }));
        BenchPlayer me = await kit.EnterAsync(name: "KuK", level: 1, classId: HumanClasses.Fighter);

        Assert.Null(await me.BypassAsync(0, "admin_setlevel 40"));
        Assert.Equal(40, me.Player.Level);

        // Human Knight = 4 (first profession from Fighter).
        Assert.Null(await me.BypassAsync(0, "admin_setclass 4"));
        Assert.Equal(4, me.Player.ClassId);
    }

    [Fact]
    public async Task Arcana_SA_and_Draconic_set_are_buyable()
    {
        // Live catalogs: S weapons 9998 (Arcana Acumen) + S sets 9956 (Draconic Leather).
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        Assert.True(kit.Game.Items.TryGet(6608, out _));
        Assert.True(kit.Game.Items.TryGet(6379, out _));

        Assert.Null(await me.BypassAsync(0, "admin_buy 9998"));
        Assert.Null(await me.Buy(0, 9998, (6608, 1)));
        Assert.True(me.Inventory.CountOf(6608) >= 1);

        Assert.Null(await me.BypassAsync(0, "admin_buy 9956"));
        Assert.Null(await me.Buy(0, 9956, (6379, 1)));
        Assert.True(me.Inventory.CountOf(6379) >= 1);
    }

    [Fact]
    public async Task Shots_buylist_and_mbuff_work_for_KuK()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        Assert.True(kit.Game.Items.TryGet(1835, out _));
        Assert.True(kit.Game.Items.TryGet(1465, out _)); // B-grade was missing from items.json

        Assert.Null(await me.BypassAsync(0, "admin_buy 9938"));
        Assert.Null(await me.Buy(0, 9938, (1835, 1)));
        Assert.True(me.Inventory.CountOf(1835) >= 1);

        Assert.Null(await me.Buy(0, 9938, (1465, 100)));
        Assert.True(me.Inventory.CountOf(1465) >= 100);

        Assert.Null(await me.Buy(0, 9938, (1835, 10000)));
        Assert.True(me.Inventory.CountOf(1835) >= 10001);

        Assert.Null(await me.BypassAsync(0, "admin_mbuff"));
        Assert.True(me.Player.Abnormals.Count >= 3);
        long packNow = kit.Now().ToUnixTimeMilliseconds();
        var adminSong = Assert.Single(me.Player.Abnormals, a => a.Id == 268);
        Assert.Equal(packNow + ZoneBuffs.AdminPackDurationSeconds * 1000L, adminSong.ExpiresAtUnixMs);
        // Expanded mage pack: SWS Song of Wind + BD dances must land.
        Assert.Contains(me.Player.Abnormals, a => a.Id == 268);
        Assert.Contains(me.Player.Abnormals, a => a.Id == 276);
        Assert.Contains(me.Player.Abnormals, a => a.Id == 365);
    }

    [Fact]
    public async Task Fbuff_adds_song_of_wind_and_bers_for_an_hour_real_song_stays_default()
    {
        Assert.Contains(268, ZoneBuffs.FighterSelfBuffs);
        Assert.Contains(1062, ZoneBuffs.FighterSelfBuffs);

        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        Assert.Null(await me.BypassAsync(0, "admin_fbuff"));

        long now = kit.Now().ToUnixTimeMilliseconds();
        var song = Assert.Single(me.Player.Abnormals, a => a.Id == 268);
        var bers = Assert.Single(me.Player.Abnormals, a => a.Id == 1062);
        Assert.Equal(now + ZoneBuffs.AdminPackDurationSeconds * 1000L, song.ExpiresAtUnixMs);
        Assert.Equal(now + ZoneBuffs.AdminPackDurationSeconds * 1000L, bers.ExpiresAtUnixMs);

        var template = kit.Game.Skills.Find(268);
        Assert.NotNull(template);
        Assert.Equal(120, template!.AbnormalTimeSeconds);
        Assert.True(template.TryLevelData(1, out var level));
        var sung = new ZoneEntity { ObjectId = 99, Kind = ZoneEntityKind.Player };
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, level, sung));
        Assert.Equal(now + 120_000L, Assert.Single(sung.Abnormals).ExpiresAtUnixMs);
    }

    [Fact]
    public async Task Mana_potion_and_thief_key_are_on_the_gm_shop()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        Assert.True(kit.Game.Items.TryGet(728, out _), "Mana Potion 728 missing from items.json");
        Assert.True(kit.Game.Items.TryGet(1661, out _), "Thief Key 1661 missing from items.json");

        int potions = me.Inventory.CountOf(728);
        int keys = me.Inventory.CountOf(1661);
        Assert.Null(await me.BypassAsync(0, "admin_buy 9928"));
        Assert.Null(await me.Buy(0, 9928, (728, 10)));
        Assert.Equal(potions + 10, me.Inventory.CountOf(728));

        Assert.Null(await me.BypassAsync(0, "admin_buy 9978"));
        Assert.Null(await me.Buy(0, 9978, (1661, 20)));
        Assert.Equal(keys + 20, me.Inventory.CountOf(1661));
    }

    [Fact]
    public async Task Gm_shop_arrows_buy_any_quantity_and_stack_into_the_existing_row()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        foreach (int id in new[] { 17, 1341, 1342, 1343, 1344, 1345, 8192 })
        {
            Assert.True(kit.Game.Items.TryGet(id, out var template), $"arrow {id} missing from items.json");
            Assert.True(template!.Stackable, $"arrow {id} must be stackable");
            Assert.Equal(Fineage.Game.EtcItemType.Arrow, template.EtcType);
        }

        Assert.Null(await me.BypassAsync(0, "admin_buy 9979"));
        int before = me.Inventory.CountOf(17);
        Assert.Null(await me.Buy(0, 9979, (17, 2500)));
        Assert.Equal(before + 2500, me.Inventory.CountOf(17));
        Assert.Single(me.Inventory.Items, item => item.ItemId == 17);

        Assert.Null(await me.Buy(0, 9979, (17, 500)));
        Assert.Equal(before + 3000, me.Inventory.CountOf(17));
        Assert.Single(me.Inventory.Items, item => item.ItemId == 17);

        int bone = me.Inventory.CountOf(1341);
        int steel = me.Inventory.CountOf(1342);
        int silver = me.Inventory.CountOf(1343);
        int mithril = me.Inventory.CountOf(1344);
        int shining = me.Inventory.CountOf(1345);
        int breaking = me.Inventory.CountOf(8192);
        Assert.Null(await me.Buy(0, 9979, (1341, 100), (1342, 100), (1343, 100), (1344, 100), (1345, 1000), (8192, 50)));
        Assert.Equal(bone + 100, me.Inventory.CountOf(1341));
        Assert.Equal(steel + 100, me.Inventory.CountOf(1342));
        Assert.Equal(silver + 100, me.Inventory.CountOf(1343));
        Assert.Equal(mithril + 100, me.Inventory.CountOf(1344));
        Assert.Equal(shining + 1000, me.Inventory.CountOf(1345));
        Assert.Equal(breaking + 50, me.Inventory.CountOf(8192));
        Assert.Single(me.Inventory.Items, item => item.ItemId == 1345);
    }

    [Fact]
    public async Task Bought_mana_potion_restores_mp()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        Assert.Null(await me.BypassAsync(0, "admin_buy 9928"));
        int before = me.Inventory.CountOf(728);
        Assert.Null(await me.Buy(0, 9928, (728, 3)));
        Assert.Equal(before + 3, me.Inventory.CountOf(728));
        Assert.Single(me.Inventory.Items, item => item.ItemId == 728);

        me.Player.CurMp = 0;
        Assert.Null(await me.Use(me.Slot(728)));
        Assert.Equal(System.Math.Min(me.Player.MaxMp, 1000), me.Player.CurMp);
        Assert.Equal(before + 2, me.Inventory.CountOf(728));
    }

    [Fact]
    public async Task Admin_spawn_places_the_npc_in_front_of_the_gm()
    {
        const int ChestId = 18268; // Treasure Chest lv 30
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        me.Player.Heading = 0;
        int x0 = me.Player.X;
        int y0 = me.Player.Y;
        int z0 = me.Player.Z;
        Assert.Equal(0, kit.World.CountNpcTemplateForTest(ChestId));

        Assert.Null(await me.BypassAsync(0, "admin_spawn 18268"));
        var chest = kit.World.FindNpcTemplateForTest(ChestId);
        Assert.NotNull(chest);
        Assert.Equal(ChestId, chest!.NpcId);
        Assert.Equal("Chest", chest.NpcType, ignoreCase: true);
        Assert.Equal(30, chest.Level);
        Assert.True(chest.Attackable);
        var expected = Fineage.Zone.Combat.ZoneCombat.PointAlongHeading(x0, y0, 0, 80);
        Assert.Equal(expected.X, chest.X);
        Assert.Equal(expected.Y, chest.Y);
        Assert.Equal(z0, chest.Z);
        Assert.Contains(chest.ObjectId, me.Player.Knownlist);

        // One-shot: no respawn book entry, so a dead chest stays gone.
        kit.World.DespawnNpcForTests(chest.ObjectId);
        kit.World.AdvanceSpawnsForTests();
        Assert.Null(kit.World.FindNpcTemplateForTest(ChestId));
    }

    [Fact]
    public async Task Admin_spawn_monster_alias_spawns_the_requested_count()
    {
        const int ChestId = 18268;
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        me.Player.Heading = 0;
        Assert.Null(await me.BypassAsync(0, "admin_spawn_monster 18268 3"));
        Assert.Equal(3, kit.World.CountNpcTemplateForTest(ChestId));
    }

    [Fact]
    public async Task Admin_spawn_refuses_an_unknown_npc_and_a_non_admin()
    {
        var kit = new WorldBench();
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        Assert.Equal("bad_request", await me.BypassAsync(0, "admin_spawn 99999999"));
        Assert.Equal("bad_request", await me.BypassAsync(0, "admin_spawn"));

        BenchPlayer other = await kit.EnterAsync(name: "NotAdmin");
        Assert.Equal("forbidden", await other.BypassAsync(0, "admin_spawn 18268"));
    }
}
