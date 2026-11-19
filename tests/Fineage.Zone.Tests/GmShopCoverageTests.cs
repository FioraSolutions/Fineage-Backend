using System.Text.RegularExpressions;
using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.NpcHtml;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Every GM-shop page under data/html/admin/gmshops.htm and gmstore/** is reachable,
/// every admin_buy list exists, every row has a catalog template, and admin_buy grants
/// the item (stack, unique, gear, arrows, consumables).
/// </summary>
[Collection("Zone admin gate")]
public sealed class GmShopCoverageTests : IDisposable
{
    const int AdminBuyBatch = 40;

    static readonly Regex BuyCommand = new(@"admin_buy\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex HtmlCommand = new(@"admin_html\s+([A-Za-z0-9_./-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly HashSet<string> SoftEquipRefusals =
    [
        InventoryProtocol.ClassRestricted,
        InventoryProtocol.RaceRestricted,
        InventoryProtocol.SexRestricted,
        InventoryProtocol.HeroRestricted,
        InventoryProtocol.ClanLeaderRestricted,
        InventoryProtocol.PledgeRestricted,
        InventoryProtocol.PvpRestricted,
        InventoryProtocol.LevelRestricted,
        InventoryProtocol.CastleRestricted,
        InventoryProtocol.ClanHallRestricted,
        InventoryProtocol.PkRestricted,
        InventoryProtocol.GradeRestricted,
        InventoryProtocol.ConditionRestricted,
        InventoryProtocol.Blocked
    ];

    readonly string? _prevEnv;
    readonly ItemSkillCatalog _itemSkills = ItemSkillCatalog.LoadDefault();

    public GmShopCoverageTests()
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
    public void Gm_shop_html_pages_and_buylists_all_exist()
    {
        var html = NpcHtmlCatalog.LoadDefault();
        Assert.True(html.Contains("admin/gmshops.htm"), "admin/gmshops.htm is missing from the HTML catalog");
        Assert.True(html.Contains("admin/gmstore/gmstore.htm") || html.Contains("admin/gmshops.htm"));

        var pages = EnumerateGmShopPages();
        Assert.True(pages.Count >= 20, "expected the gmstore tree, got " + pages.Count);

        var missingPages = new List<string>();
        var buyIds = new SortedSet<int>();
        foreach (var (relative, text) in pages)
        {
            foreach (Match match in HtmlCommand.Matches(text))
            {
                string target = "admin/" + match.Groups[1].Value.Trim().TrimStart('/');
                if (!html.Contains(target) && !html.Contains(match.Groups[1].Value.Trim()))
                    missingPages.Add(relative + " -> " + match.Groups[1].Value);
            }

            foreach (Match match in BuyCommand.Matches(text))
                buyIds.Add(int.Parse(match.Groups[1].Value));
        }

        Assert.True(missingPages.Count == 0, "broken admin_html links: " + string.Join(", ", missingPages));
        Assert.DoesNotContain(10007, buyIds);

        var lists = Fineage.Game.Shops.BuyListCatalog.LoadDefault();
        var missingLists = buyIds.Where(id => !lists.TryGet(id, out _)).ToList();
        Assert.True(missingLists.Count == 0, "admin_buy lists missing from buylists.json: " + string.Join(", ", missingLists));
    }

    [Fact]
    public void Every_gm_shop_item_has_a_catalog_template()
    {
        var data = GameData.LoadDefault();
        var lists = Fineage.Game.Shops.BuyListCatalog.LoadDefault();
        var missing = new List<string>();
        foreach (var (listId, itemId) in EnumerateGmShopItems(lists))
        {
            if (!data.Items.TryGet(itemId, out var template))
            {
                missing.Add(listId + ":" + itemId);
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(template.Name), "item " + itemId + " has no name");
            Assert.False(string.IsNullOrWhiteSpace(template.Icon), "item " + itemId + " has no icon");
            if (template.Kind == ItemKind.Weapon)
            {
                Assert.NotEqual(WeaponType.None, template.WeaponType);
                Assert.NotEqual(BodySlot.None, template.BodySlot);
            }
            else if (template.Kind == ItemKind.Armor)
            {
                Assert.NotEqual(BodySlot.None, template.BodySlot);
            }
        }

        Assert.True(missing.Count == 0, "GM shop items missing from items.json: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Every_gm_shop_item_buys_and_behaves()
    {
        var kit = new WorldBench(expertisePenalty: false);
        BenchPlayer me = await kit.EnterAsync(name: "KuK", level: 80);
        var lists = Fineage.Game.Shops.BuyListCatalog.LoadDefault();
        var entries = EnumerateGmShopItems(lists).ToList();
        Assert.True(entries.Count > 4000, "expected the full GM shop, got " + entries.Count);

        var seen = new HashSet<int>();
        int openList = -1;
        int sinceClear = 0;
        var failures = new List<string>();

        foreach (var (listId, itemId) in entries)
        {
            if (!seen.Add(itemId))
                continue;
            if (!kit.Game.Items.TryGet(itemId, out var template))
            {
                failures.Add(itemId + " missing template");
                continue;
            }

            if (openList != listId)
            {
                string? opened = await me.BypassAsync(0, "admin_buy " + listId);
                if (opened != null)
                {
                    failures.Add("admin_buy " + listId + " -> " + opened);
                    continue;
                }

                openList = listId;
            }

            if (sinceClear >= AdminBuyBatch)
            {
                ClearPurchases(me, keep: null);
                sinceClear = 0;
                openList = listId; // shop session still open
            }

            if (template.Stackable)
                await AssertStackableAsync(me, listId, itemId, failures);
            else
                await AssertUniqueAsync(me, listId, itemId, failures);

            if (template.EtcType == EtcItemType.Arrow)
                await AssertArrowEquipsAsync(me, itemId, failures);
            else if (template.Equippable)
                await AssertEquippableAsync(me, itemId, template, failures);
            else if (_itemSkills.TryGet(itemId, out _))
                await AssertConsumableUseAsync(me, itemId, failures);

            sinceClear++;
        }

        Assert.True(failures.Count == 0, "GM shop buy/use failures (" + failures.Count + "): " + string.Join("; ", failures.Take(40)));
        Assert.Equal(entries.Select(e => e.ItemId).Distinct().Count(), seen.Count);
    }

    async Task AssertStackableAsync(BenchPlayer me, int listId, int itemId, List<string> failures)
    {
        int before = me.Inventory.CountOf(itemId);
        int rowsBefore = me.Inventory.Items.Count(item => item.ItemId == itemId);
        string? first = await me.Buy(0, listId, (itemId, 7));
        if (first != null)
        {
            failures.Add("buy stack " + itemId + " x7 -> " + first);
            return;
        }

        if (me.Inventory.CountOf(itemId) != before + 7)
            failures.Add("buy stack " + itemId + " x7 count " + me.Inventory.CountOf(itemId) + " expected " + (before + 7));

        string? second = await me.Buy(0, listId, (itemId, 13));
        if (second != null)
        {
            failures.Add("buy stack " + itemId + " x13 -> " + second);
            return;
        }

        if (me.Inventory.CountOf(itemId) != before + 20)
            failures.Add("buy stack " + itemId + " x20 count " + me.Inventory.CountOf(itemId));
        int rows = me.Inventory.Items.Count(item => item.ItemId == itemId);
        if (rows != Math.Max(1, rowsBefore) && rows != 1)
            failures.Add("buy stack " + itemId + " split into " + rows + " rows");
    }

    async Task AssertUniqueAsync(BenchPlayer me, int listId, int itemId, List<string> failures)
    {
        int before = me.Inventory.CountOf(itemId);
        string? err = await me.Buy(0, listId, (itemId, 2));
        if (err != null)
        {
            failures.Add("buy unique " + itemId + " x2 -> " + err);
            return;
        }

        if (me.Inventory.CountOf(itemId) != before + 2)
            failures.Add("buy unique " + itemId + " count " + me.Inventory.CountOf(itemId));
        int rows = me.Inventory.Items.Count(item => item.ItemId == itemId);
        if (rows < before + 2 && rows < 2)
            failures.Add("buy unique " + itemId + " expected separate rows, got " + rows);
    }

    async Task AssertEquippableAsync(BenchPlayer me, int itemId, ItemTemplate template, List<string> failures)
    {
        UnequipAll(me);
        var item = me.Inventory.Items.FirstOrDefault(i => i.ItemId == itemId && !i.IsEquipped);
        if (item == null)
        {
            failures.Add("equip " + itemId + " not in bag");
            return;
        }

        string? err = await me.Use(item.ObjectId);
        if (err != null)
        {
            if (!SoftEquipRefusals.Contains(err))
                failures.Add("equip " + itemId + " (" + template.Name + ") -> " + err);
            return;
        }

        var equipped = me.Inventory.Items.First(i => i.ObjectId == item.ObjectId);
        if (!equipped.IsEquipped)
            failures.Add("equip " + itemId + " Use succeeded but item is not equipped");
        else
            me.Inventory.Unequip(equipped);
    }

    async Task AssertArrowEquipsAsync(BenchPlayer me, int itemId, List<string> failures)
    {
        UnequipAll(me);
        var item = me.Inventory.Items.FirstOrDefault(i => i.ItemId == itemId);
        if (item == null)
        {
            failures.Add("arrow " + itemId + " not in bag");
            return;
        }

        string? err = await me.Use(item.ObjectId);
        if (err != null)
        {
            failures.Add("arrow " + itemId + " equip -> " + err);
            return;
        }

        var quiver = me.Inventory.GetEquipped(PaperdollSlot.LHand);
        if (quiver == null || quiver.ItemId != itemId)
            failures.Add("arrow " + itemId + " did not enter the left-hand quiver");
        else
            me.Inventory.Unequip(quiver);
    }

    async Task AssertConsumableUseAsync(BenchPlayer me, int itemId, List<string> failures)
    {
        var item = me.Inventory.Items.FirstOrDefault(i => i.ItemId == itemId);
        if (item == null)
        {
            failures.Add("use " + itemId + " not in bag");
            return;
        }

        int before = me.Inventory.CountOf(itemId);
        string? err = await me.Use(item.ObjectId);
        if (err == "bad_request")
            failures.Add("use consumable " + itemId + " -> bad_request");
        // Soft refusals (full MP, reuse, missing target) are acceptable; the item must remain usable.
        if (err == null && me.Inventory.CountOf(itemId) > before)
            failures.Add("use consumable " + itemId + " increased the stack");
    }

    static void UnequipAll(BenchPlayer me)
    {
        foreach (var item in me.Inventory.Items.Where(i => i.IsEquipped).ToList())
            me.Inventory.Unequip(item);
    }

    static void ClearPurchases(BenchPlayer me, int? keep)
    {
        UnequipAll(me);
        foreach (var item in me.Inventory.Items.ToList())
        {
            if (keep != null && item.ItemId == keep)
                continue;
            // Leave the starter-kit leftovers alone when they were not part of this purchase
            // only if we somehow still need them — GM grants ignore the 80-slot cap, but
            // clearing keeps Use/Slot assertions unique.
            if (item.Template.Stackable)
                me.Inventory.TryRemoveFromStack(item, item.Count, out _);
            else
                me.Inventory.Remove(item);
        }
    }


    static List<(string Relative, string Text)> EnumerateGmShopPages()
    {
        string data = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException("data directory not found");
        string admin = Path.Combine(data, "html", "admin");
        var pages = new List<(string, string)>();
        string root = Path.Combine(admin, "gmshops.htm");
        if (File.Exists(root))
            pages.Add(("admin/gmshops.htm", File.ReadAllText(root)));
        string store = Path.Combine(admin, "gmstore");
        if (Directory.Exists(store))
        {
            foreach (string path in Directory.EnumerateFiles(store, "*.htm", SearchOption.AllDirectories))
            {
                string relative = "admin/" + Path.GetRelativePath(admin, path).Replace('\\', '/');
                pages.Add((relative, File.ReadAllText(path)));
            }
        }

        return pages;
    }

    static IEnumerable<(int ListId, int ItemId)> EnumerateGmShopItems(Fineage.Game.Shops.BuyListCatalog lists)
    {
        var buyIds = new SortedSet<int>();
        foreach (var (_, text) in EnumerateGmShopPages())
        {
            foreach (Match match in BuyCommand.Matches(text))
                buyIds.Add(int.Parse(match.Groups[1].Value));
        }

        foreach (int listId in buyIds)
        {
            if (!lists.TryGet(listId, out var list))
                continue;
            foreach (var product in list.Products)
                yield return (listId, product.ItemId);
        }
    }
}
