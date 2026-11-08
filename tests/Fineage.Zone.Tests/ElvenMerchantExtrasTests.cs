using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Fineage.Zone.NpcHtml;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ElvenMerchantExtrasTests
{
    [Fact]
    public void Merchant_page_commands_all_have_handlers()
    {
        var reg = BypassRegistry.CreateDefault(NpcHtmlCatalog.LoadDefault());
        Assert.IsType<TerritoryBypassHandler>(reg.Find("TerritoryStatus"));
        Assert.IsType<ShopBypassHandler>(reg.Find("exc_multisell 003"));
        Assert.NotNull(reg.Find("Wear 3014700"));
    }

    [Fact]
    public async Task Territory_status_for_unowned_oren_shows_no_lord()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(kit.World.NpcServiceForTest(session, 30147, NpcServiceIntent.TerritoryStatus));
        var html = ((NpcHtmlNetMsg)rec.Of(nameof(IWorldEventPublisher.FanoutNpcHtml)).Last()[1]!).Html;
        Assert.Contains("Oren", html);
        Assert.Contains("Elven Village", html);
        Assert.Contains("not ruled by any clan", html);
    }
}

public sealed class QuestItemLockTests
{
    [Fact]
    public async Task Quest_item_cannot_be_dropped_on_the_ground()
    {
        var kit = new NetTestKit();
        var (session, elf) = await kit.EnterAsync();
        var club = elf.Inventory!.AddPieces(1335, 1)[0];
        var error = await kit.World.TryDropItemAsync(session, club.ObjectId, 1, 0, 0, 0, CancellationToken.None);
        Assert.Equal("bad_request", error);
        Assert.Equal(1, elf.Inventory.CountOf(1335));
    }
}
