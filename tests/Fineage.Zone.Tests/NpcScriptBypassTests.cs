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
}
