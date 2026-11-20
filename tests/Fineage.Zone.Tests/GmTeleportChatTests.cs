using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("Zone admin gate")]
public sealed class GmTeleportChatTests : IDisposable
{
    readonly string? _prevEnv;

    public GmTeleportChatTests()
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

    [Theory]
    [InlineData("//teleport 1 2 3", "teleport", "1 2 3")]
    [InlineData("//tele 10 -20 30", "tele", "10 -20 30")]
    [InlineData("//goto Alice", "goto", "Alice")]
    [InlineData("//loc", "loc", "")]
    public void Command_parser_splits_verb_and_args(string line, string verb, string args) =>
        Assert.True(GmChatCommandParser.TrySplit(line, out string gotVerb, out string gotArgs)
            && verb.Equals(gotVerb, StringComparison.OrdinalIgnoreCase)
            && args == gotArgs);

    [Fact]
    public async Task Non_admin_gets_rejection_and_no_teleport()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer me = await kit.EnterAsync(name: "NotGm");
        int beforeX = me.Player.X;

        Assert.Null(await kit.World.TrySayAsync(me.Session, "//loc", ChatProtocol.All, kit.Now(), CancellationToken.None));
        Assert.Equal(beforeX, me.Player.X);
        Assert.Empty(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay)));
        var sys = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Contains("not authorized", sys.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Loc_replies_with_coordinates_and_never_broadcasts_chat()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer me = await kit.EnterAsync(name: "KuK");
        me.MoveTo(123, 456, -789);
        me.Player.Heading = 77;

        Assert.Null(await kit.World.TrySayAsync(me.Session, "//loc", ChatProtocol.All, kit.Now(), CancellationToken.None));
        Assert.Empty(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay)));
        var sys = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Contains("123 456 -789", sys.Text, StringComparison.Ordinal);
        Assert.Contains("heading 77", sys.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teleport_moves_gm_and_fans_TeleportToLocation()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");
        int tx = gm.Player.X + 500;
        int ty = gm.Player.Y;
        int tz = gm.Player.Z;

        Assert.Null(await kit.World.TrySayAsync(gm.Session, $"//teleport {tx} {ty} {tz}", ChatProtocol.All, kit.Now(), CancellationToken.None));
        Assert.Equal(tx, gm.Player.X);
        Assert.Equal(ty, gm.Player.Y);
        Assert.Equal(tz, gm.Player.Z);
        Assert.Empty(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay)));

        var frames = kit.Rec.Of(nameof(IWorldEventPublisher.FanoutTeleportToLocation));
        var last = frames[^1];
        Assert.Equal(gm.Player.ObjectId, (int)last[1]!);
        Assert.Equal(tx, (int)last[2]!);
        Assert.Equal(ty, (int)last[3]!);
        Assert.Equal(tz, (int)last[4]!);
    }

    [Fact]
    public async Task Goto_and_recall_relocate_both_players()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");
        BenchPlayer mate = await kit.EnterAsync(name: "Player2");
        mate.MoveTo(gm.Player.X + 800, gm.Player.Y + 100, gm.Player.Z);

        Assert.Null(await kit.World.TrySayAsync(gm.Session, "//goto Player2", ChatProtocol.All, kit.Now(), CancellationToken.None));
        Assert.Equal(mate.Player.X, gm.Player.X);
        Assert.Equal(mate.Player.Y, gm.Player.Y);

        mate.MoveTo(gm.Player.X + 1200, gm.Player.Y, gm.Player.Z);
        Assert.Null(await kit.World.TrySayAsync(gm.Session, "//recall Player2", ChatProtocol.All, kit.Now(), CancellationToken.None));
        Assert.Equal(gm.Player.X, mate.Player.X);
        Assert.Equal(gm.Player.Y, mate.Player.Y);
    }

    [Fact]
    public async Task Town_gludio_uses_datapack_coords()
    {
        var town = GmTeleportCatalog.LoadDefault().Find("gludio");
        Assert.NotNull(town);

        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");

        Assert.Null(await kit.World.TrySayAsync(gm.Session, "//town gludio", ChatProtocol.All, kit.Now(), CancellationToken.None));
        Assert.Equal(town!.X, gm.Player.X);
        Assert.Equal(town.Y, gm.Player.Y);
        Assert.Equal(town.Z, gm.Player.Z);
    }

    [Fact]
    public async Task Unknown_town_and_offline_player_return_messages()
    {
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");

        Assert.Null(await kit.World.TrySayAsync(gm.Session, "//town nowhere", ChatProtocol.All, kit.Now(), CancellationToken.None));
        var sys = Assert.IsType<SystemMessageMsg>(fullKitRec(kit).Last()[1]);
        Assert.Contains("Unknown town", sys.Text, StringComparison.Ordinal);

        Assert.Null(await kit.World.TrySayAsync(gm.Session, "//goto NobodyOnline", ChatProtocol.All, kit.Now(), CancellationToken.None));
        sys = Assert.IsType<SystemMessageMsg>(fullKitRec(kit).Last()[1]);
        Assert.Contains("not online", sys.Text, StringComparison.OrdinalIgnoreCase);
    }

    static System.Collections.Generic.List<object?[]> fullKitRec(WorldBench kit) =>
        kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage));
}

/// <summary>Test mirror of GM chat line parsing.</summary>
static file class GmChatCommandParser
{
    public static bool TrySplit(string text, out string verb, out string args)
    {
        const string prefix = "//";
        verb = "";
        args = "";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string body = text[prefix.Length..].TrimStart();
        if (body.Length == 0)
            return false;
        int space = body.IndexOf(' ');
        if (space < 0)
        {
            verb = body;
            return true;
        }

        verb = body[..space];
        args = body[(space + 1)..].TrimStart();
        return true;
    }
}