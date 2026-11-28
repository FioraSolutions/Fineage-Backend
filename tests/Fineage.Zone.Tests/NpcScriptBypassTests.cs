using Fineage.Net.Contract;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NpcScriptBypassTests
{
    [Fact]
    public async Task Unknown_script_shows_unavailable_and_logs_once()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();
        Assert.Null(kit.World.NpcScriptBypassForTest(session, 30154, "NotARealMobiusScript", "", out var html));
        Assert.Contains("not implemented", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, kit.World.UnknownNpcScriptLogCount);
    }

    [Fact]
    public async Task RaidbossInfo_opens_info_page_for_adventurer_guild()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();
        Assert.Null(kit.World.NpcScriptBypassForTest(session, 31775, "RaidbossInfo", "", out var html));
        Assert.Contains("Raid Monster Information", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("level20.htm", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RaidbossInfo_level_page_lists_raids()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();
        Assert.Null(kit.World.NpcScriptBypassForTest(session, 31776, "RaidbossInfo", "level20.htm", out var html));
        Assert.Contains("25372", html, StringComparison.Ordinal);
        Assert.Contains("Greyclaw", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RaidbossInfo_boss_id_places_radar_marker()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (session, _) = await kit.EnterAsync();

        Assert.Null(kit.World.NpcScriptBypassForTest(session, 31775, "RaidbossInfo", "25001", out _));

        var dir = Assert.IsType<QuestDirectionNetMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutQuestDirection)).Last()[1]);
        Assert.True(dir.Show);
        Assert.NotEqual(0, dir.X);
        Assert.NotEqual(0, dir.Y);
    }

    [Fact]
    public async Task NpcLocationInfo_opens_guide_root_for_newbie_guide()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();
        Assert.Null(kit.World.NpcScriptBypassForTest(session, 30598, "NpcLocationInfo", "", out var html));
        Assert.Contains("NpcLocationInfo", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Teleporter", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NpcLocationInfo_digit_event_places_radar_and_MoveToLoc()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (session, _) = await kit.EnterAsync();

        Assert.Null(kit.World.NpcScriptBypassForTest(session, 30598, "NpcLocationInfo", "30006", out var html));
        Assert.Contains("direction of the arrow", html, StringComparison.OrdinalIgnoreCase);

        var dir = Assert.IsType<QuestDirectionNetMsg>(
            rec.Of(nameof(IWorldEventPublisher.FanoutQuestDirection)).Last()[1]);
        Assert.True(dir.Show);
        Assert.NotEqual(0, dir.X);
        Assert.NotEqual(0, dir.Y);
    }

    [Fact]
    public async Task ShadowWeapons_without_coupon_shows_exchange_no()
    {
        var kit = new NetTestKit();
        var (session, _) = await kit.EnterAsync();
        Assert.Null(kit.World.NpcScriptBypassForTest(session, 30037, "ShadowWeapons", "", out var html));
        Assert.Contains("exchange coupon", html, StringComparison.OrdinalIgnoreCase);
    }
}
