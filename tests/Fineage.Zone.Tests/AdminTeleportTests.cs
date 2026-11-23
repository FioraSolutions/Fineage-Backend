using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>AdminTeleport</c> bypasses, as the admin teleport pages send them (issue #950).</summary>
[Collection("Zone admin gate")]
public sealed class AdminTeleportTests : IDisposable
{
    readonly string? _prevEnv;

    public AdminTeleportTests()
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

    // Giran town square (Mobius admin/teleports/TownAreas).
    const int GiranX = 83400, GiranY = 147943, GiranZ = -3404;

    [Fact]
    public async Task Move_to_relocates_the_gm_and_the_menus_open()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");

        Assert.Null(await gm.BypassAsync(0, $"bypass -h admin_move_to {GiranX} {GiranY} {GiranZ}"));
        Assert.Equal(GiranX, gm.Player.X);
        Assert.Equal(GiranY, gm.Player.Y);

        Assert.Null(await gm.BypassAsync(0, "admin_goeast 200"));
        Assert.Equal(GiranX + 200, gm.Player.X);
        Assert.Contains("admin_gonorth", kit.World.LastNpcHtmlForTest, StringComparison.Ordinal);

        Assert.Null(await gm.BypassAsync(0, "admin_show_moves"));
        Assert.Contains("teleports/TownAreas.htm", kit.World.LastNpcHtmlForTest, StringComparison.Ordinal);
        Assert.Null(await gm.BypassAsync(0, "admin_html teleports/TownAreas/giran.htm"));
        Assert.Contains("admin_move_to", kit.World.LastNpcHtmlForTest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teleportto_and_recall_move_between_players()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");
        BenchPlayer other = await kit.EnterAsync(name: "Alice", x: GiranX, y: GiranY, z: GiranZ);

        Assert.Null(await gm.BypassAsync(0, "admin_teleportto Alice"));
        Assert.Equal(GiranX, gm.Player.X);

        Assert.Null(await gm.BypassAsync(0, "admin_move_to 46045 41251 -3440"));
        Assert.Null(await gm.BypassAsync(0, "admin_recall Alice"));
        Assert.Equal(46045, other.Player.X);
        Assert.Equal("target", await gm.BypassAsync(0, "admin_recall Nobody"));
    }

    [Fact]
    public async Task Unknown_teleport_verbs_refuse_and_name_the_real_ones()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");
        int x = gm.Player.X;

        Assert.Equal("bad_request", await gm.BypassAsync(0, "admin_teleport 1 2 3"));
        Assert.Equal(x, gm.Player.X);
        var sys = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Contains("admin_move_to", sys.Text, StringComparison.Ordinal);

        Assert.Equal("bad_request", await gm.BypassAsync(0, "admin_move_to 1 two 3"));
        Assert.Equal(
            NpcServiceProtocol.TeleportRefusalCode(NpcServiceProtocol.DestinationUnloaded),
            await gm.BypassAsync(0, "admin_move_to -300000 0 0"));
        Assert.Equal(x, gm.Player.X);
    }
}
