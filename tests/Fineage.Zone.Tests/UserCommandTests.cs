using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class UserCommandTests
{
    [Fact]
    public void BypassUserCmd_round_trips_the_command_id()
    {
        var back = NetCodec.DecodeBypassUserCmd(NetCodec.EncodeBypassUserCmd(new BypassUserCmdNetMsg { CommandId = UserCommandProtocol.Unstuck }));
        Assert.Equal(UserCommandProtocol.Unstuck, back.CommandId);
        Assert.Equal(0x03E2, NetOpcode.BypassUserCmd);
    }

    [Fact]
    public async Task Unknown_command_is_rejected_and_does_not_move()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        int x = me.Player.X;

        Assert.Equal(UserCommandProtocol.Unknown, await kit.World.TryUserCommandAsync(me.Session, 12345, CancellationToken.None));
        Assert.Equal(x, me.Player.X);
    }

    [Fact]
    public async Task Unstuck_uses_the_town_teleport_cast()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        var region = MapRegionCatalog.LoadDefault().At(me.Player.X, me.Player.Y);
        Assert.NotNull(region);
        me.MoveTo(me.Player.X + 100, me.Player.Y + 100, me.Player.Z);
        int originX = me.Player.X;

        Assert.Null(await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.Unstuck, CancellationToken.None));
        Assert.Equal(originX, me.Player.X);
        Assert.True(me.Player.CastingUntilMs > 0);
        var cast = Assert.IsType<MagicSkillUseMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)).Last()[1]);
        Assert.Equal(2099, cast.SkillId);
        Assert.Equal(UserCommandProtocol.PlayerUnstuckMs, cast.HitTimeMs);
        Assert.Contains(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            row => Assert.IsType<SystemMessageMsg>(row[1]).Id == UserCommandProtocol.UnstuckFiveMinutes);

        await kit.World.CompleteCastForTestAsync(me.Session);
        Assert.Contains(region!.RespawnPoints, p => p.X == me.Player.X && p.Y == me.Player.Y && p.Z == me.Player.Z && p.Kind == "normal");
    }

    [Fact]
    public async Task Unstuck_while_dead_does_not_teleport()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        me.Player.Dead = true;
        int x = me.Player.X;

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.Unstuck, CancellationToken.None));
        Assert.Equal(x, me.Player.X);
        Assert.Empty(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)));
    }

    [Fact]
    public async Task Loc_sends_the_region_message_with_coordinates()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();
        int locId = MapRegionCatalog.LoadDefault().At(me.Player.X, me.Player.Y)!.LocId;

        Assert.Null(await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.Loc, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(locId, line.Id);
        Assert.Equal(me.Player.X.ToString(), line.Params[0]);
        Assert.Equal(me.Player.Y.ToString(), line.Params[1]);
        Assert.Equal(me.Player.Z.ToString(), line.Params[2]);
    }

    [Fact]
    public async Task Time_sends_the_in_game_clock()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Null(await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.Time, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.True(line.Id is UserCommandProtocol.TimeDay or UserCommandProtocol.TimeNight);
        Assert.Equal(2, line.Params.Length);
    }

    [Fact]
    public async Task Mount_and_dismount_without_a_pet_are_refused()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.Mount, CancellationToken.None));
        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.Dismount, CancellationToken.None));
        Assert.Null(me.Player.Mount);
    }

    [Fact]
    public async Task Party_info_without_a_party_sends_the_header()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Null(await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.PartyInfo, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(Assert.Single(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)))[1]);
        Assert.Equal(UserCommandProtocol.PartyInformation, line.Id);
    }

    [Fact]
    public async Task Clan_war_list_without_a_clan_is_refused()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.ClanWarList, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(UserCommandProtocol.NotInClan, line.Id);
    }

    [Fact]
    public async Task Olympiad_stat_refuses_a_non_noble()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.OlympiadStat, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(UserCommandProtocol.NoblesseOnly, line.Id);
    }

    [Fact]
    public async Task Olympiad_stat_refuses_when_the_games_are_disabled()
    {
        var kit = new WorldBench(olympiadSettings: new OlympiadSettings { Enabled = false });
        var me = await kit.EnterAsync();
        Assert.True(kit.World.Olympiad.GrantNoblesse(me.ObjectId));

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.OlympiadStat, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(UserCommandProtocol.OlympiadNotRunning, line.Id);
    }

    [Fact]
    public async Task Siege_status_refuses_a_player_who_is_not_a_noble_clan_leader()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.SiegeStatus, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(UserCommandProtocol.SiegeStatusNoblesse, line.Id);
    }

    [Fact]
    public async Task Channel_leave_without_a_party_tells_the_leader_rule()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.ChannelLeave, CancellationToken.None));
        var line = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Equal(UserCommandProtocol.ChannelLeaveLeaderOnly, line.Id);
    }

    [Fact]
    public async Task Channel_delete_and_info_without_a_channel_are_refused()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.ChannelDelete, CancellationToken.None));
        Assert.Equal(UserCommandProtocol.Refused, await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.ChannelInfo, CancellationToken.None));
        Assert.Empty(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutCommandChannelInfo)));
    }

    [Fact]
    public async Task Clan_penalty_with_no_expiry_sends_the_empty_page()
    {
        var kit = new WorldBench();
        var me = await kit.EnterAsync();

        Assert.Null(await kit.World.TryUserCommandAsync(me.Session, UserCommandProtocol.ClanPenalty, CancellationToken.None));
        var html = Assert.IsType<NpcHtmlMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutNpcHtml)).Last()[1]);
        Assert.Contains("No penalty is imposed.", html.Html, StringComparison.Ordinal);
    }
}
