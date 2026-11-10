using Fineage.Accounts;
using Fineage.Game.Shops;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius admin_gmshop / admin_buy / setlevel gated by KuK + FINEAGE_ZONE_ADMIN.</summary>
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
        // Expanded mage pack: SWS Song of Wind + BD dances must land.
        Assert.Contains(me.Player.Abnormals, a => a.Id == 268);
        Assert.Contains(me.Player.Abnormals, a => a.Id == 276);
        Assert.Contains(me.Player.Abnormals, a => a.Id == 365);
    }
}
