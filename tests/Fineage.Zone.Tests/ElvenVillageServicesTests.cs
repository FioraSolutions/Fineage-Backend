using System.Text.RegularExpressions;
using Fineage.Game.Services;
using Fineage.Zone.NpcHtml;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Elven Village NPC pages (tools/import_npc_html.py) resolve to services the server runs.</summary>
public sealed class ElvenVillageServicesTests
{
    [Fact]
    public void Mirabel_teleport_links_point_at_teleports_json_rows()
    {
        var html = NpcHtmlCatalog.LoadDefault();
        var tp = TeleportCatalog.LoadDefault();
        string page = NpcHtmlPaths.Read(html, "Teleporter", 30146, 1);
        var links = Regex.Matches(page, @"_teleport (\w+) (\d+)");
        Assert.Equal(7, links.Count);
        Assert.DoesNotContain("_goto ", page);
        foreach (Match m in links)
            Assert.NotNull(tp.Find(30146, m.Groups[1].Value)!.Find(int.Parse(m.Groups[2].Value)));
        Assert.Contains("Talking Island Village", page);
    }

    [Theory]
    [InlineData(30147, "3014700")]
    [InlineData(30148, "3014800")]
    [InlineData(30149, "3014900")]
    [InlineData(30150, "3015000")]
    public void Elven_traders_have_buy_pages(int npcId, string list)
    {
        string page = NpcHtmlPaths.Read(NpcHtmlCatalog.LoadDefault(), "Merchant", npcId, 0);
        Assert.Contains($"_Buy {list}", page);
        Assert.Contains("_Quest", page);
    }

    [Theory]
    [InlineData(30370)]
    [InlineData(30362)]
    [InlineData(30371)]
    public void Quest_npcs_have_their_own_page_with_quest_link(int npcId)
    {
        string page = NpcHtmlPaths.Read(NpcHtmlCatalog.LoadDefault(), "Folk", npcId, 0);
        Assert.DoesNotContain("I have nothing to say to you", page);
        Assert.Contains("_Quest", page);
    }
}
