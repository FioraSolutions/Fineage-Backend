using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// PvP / PK end to end over real Net sessions (Mobius CT_0 Interlude rules): Ctrl-gated attack, purple flag seen by
/// peers (blink, expiry), PvP vs PK kills, red name, karma persistence and burn. Players stand outside every peace
/// polygon; the world clock only moves through <see cref="NetMoveVisibilityRig.AdvanceAsync"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetPvpPkWireTests
{
    const int X = 49000, Y = 50000, Z = -3000;

    static CancellationToken Ct => CancellationToken.None;

    static Task SendAttackAsync(NetMoveVisibilityRig.Player who, NetMoveVisibilityRig.Player target, bool? forced) =>
        who.Net.SendAsync(NetOpcode.AttackRequest,
            NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = target.ObjectId, Forced = forced }), Ct);

    static async Task<(NetMoveVisibilityRig Rig, NetMoveVisibilityRig.Player A, NetMoveVisibilityRig.Player B,
        NetMoveVisibilityRig.Player Watcher)> TrioAsync()
    {
        var rig = await NetMoveVisibilityRig.StartAsync(Ct);
        var a = await rig.JoinAsync(X, Y, Z);
        var b = await rig.JoinAsync(X + 30, Y, Z);
        var w = await rig.JoinAsync(X + 300, Y, Z);
        await a.DrainAsync();
        await b.DrainAsync();
        await w.DrainAsync();
        b.Entity.MaxHp = b.Entity.CurHp = 100_000;
        return (rig, a, b, w);
    }

    static List<EntitySpawnMsg> SpawnsOf(IEnumerable<PacketEnvelope> frames, NetMoveVisibilityRig.Player who) =>
        frames.Where(f => f.Opcode == NetOpcode.EntitySpawn)
            .Select(f => NetCodec.DecodeEntitySpawn(f.Payload))
            .Where(s => s.EntityValue == who.EntityValue)
            .ToList();

    static List<UserInfoNetMsg> UserInfos(IEnumerable<PacketEnvelope> frames) =>
        frames.Where(f => f.Opcode == NetOpcode.UserInfo).Select(f => NetCodec.DecodeUserInfo(f.Payload)).ToList();

    /// <summary>Swing until <paramref name="done"/>: the attack loop re-swings on its own once started.</summary>
    static async Task FightUntilAsync(NetMoveVisibilityRig rig, NetMoveVisibilityRig.Player a, NetMoveVisibilityRig.Player b, Func<bool> done)
    {
        await SendAttackAsync(a, b, forced: true);
        await a.DrainAsync();
        for (int i = 0; i < 40 && !done(); i++)
            await rig.AdvanceAsync(500);
    }

    [Fact]
    public async Task A_plain_click_on_a_white_player_does_nothing_and_ctrl_flags_the_attacker_for_every_watcher()
    {
        var (rig, a, b, w) = await TrioAsync();
        await using var _ = rig;

        await SendAttackAsync(a, b, forced: false);
        await a.DrainAsync();
        await rig.AdvanceAsync(3000);
        Assert.False(a.Entity.PvpFlagged);
        Assert.Equal(100_000, b.Entity.CurHp);
        Assert.Empty(SpawnsOf(await w.DrainAsync(), a));

        await SendAttackAsync(a, b, forced: true);
        await a.DrainAsync();
        await rig.AdvanceAsync(2000);

        Assert.True(a.Entity.PvpFlagged, "a Ctrl swing at a white player raises the purple name (hit or miss)");
        Assert.False(b.Entity.PvpFlagged, "the victim is not flagged");
        var seen = SpawnsOf(await w.DrainAsync(), a);
        Assert.Equal(1, Assert.Single(seen).PvpFlag);
        Assert.Contains(UserInfos(await a.DrainAsync()), u => u.PvpFlag == 1);
    }

    [Fact]
    public async Task Legacy_attack_request_without_the_forced_byte_still_counts_as_forced()
    {
        var (rig, a, b, _) = await TrioAsync();
        await using var __ = rig;

        await SendAttackAsync(a, b, forced: null);
        await a.DrainAsync();
        await rig.AdvanceAsync(2000);
        Assert.True(a.Entity.PvpFlagged);
    }

    [Fact]
    public async Task The_flag_blinks_for_its_last_20_seconds_then_clears_for_watchers()
    {
        var (rig, a, b, w) = await TrioAsync();
        await using var _ = rig;
        await SendAttackAsync(a, b, forced: true);
        await a.DrainAsync();
        await rig.AdvanceAsync(1000);
        // Stop swinging, so nothing refreshes the timer.
        a.Entity.Intent.AutoAttackTargetId = 0;
        Assert.True(a.Entity.PvpFlagged);
        await w.DrainAsync();

        await rig.AdvanceAsync(ZoneWorld.PvpNormalTimeMs - ZoneWorld.PvpBlinkMs, stepMs: 1000);
        Assert.Equal(2, SpawnsOf(await w.DrainAsync(), a).Last().PvpFlag);

        await rig.AdvanceAsync(ZoneWorld.PvpBlinkMs + 1000, stepMs: 1000);
        Assert.False(a.Entity.PvpFlagged);
        Assert.Equal(0, SpawnsOf(await w.DrainAsync(), a).Last().PvpFlag);
    }

    [Fact]
    public async Task Hitting_back_a_flagged_attacker_flags_the_defender_too()
    {
        var (rig, a, b, _) = await TrioAsync();
        await using var _r = rig;
        a.Entity.MaxHp = a.Entity.CurHp = 100_000;
        await SendAttackAsync(a, b, forced: true);
        await a.DrainAsync();
        await rig.AdvanceAsync(1000);
        Assert.True(a.Entity.PvpFlagged);

        // A plain click works now: the attacker is purple, so auto-attackable.
        await SendAttackAsync(b, a, forced: false);
        await b.DrainAsync();
        await rig.AdvanceAsync(2000);
        Assert.True(b.Entity.PvpFlagged, "Mobius updatePvPStatus: hitting a karma-0 flagged player flags you as well");
    }

    [Fact]
    public async Task Killing_a_white_player_is_a_pk_red_name_reaches_watchers_and_is_stored()
    {
        var (rig, a, b, w) = await TrioAsync();
        await using var _ = rig;
        b.Entity.CurHp = 1;

        await FightUntilAsync(rig, a, b, () => b.Entity.Dead);

        Assert.True(b.Entity.Dead);
        Assert.Equal(1, a.Entity.PkKills);
        Assert.Equal(0, a.Entity.PvpKills);
        Assert.Equal(720, a.Entity.Karma);
        var stored = rig.Store.FindById(a.Entity.Session!.Account, a.ObjectId);
        Assert.Equal(720, stored!.Karma);
        Assert.Equal(1, stored.PkKills);
        Assert.Equal(720, SpawnsOf(await w.DrainAsync(), a).Last().Karma);
        var self = UserInfos(await a.DrainAsync()).Last();
        Assert.Equal(720, self.Karma);
        Assert.Equal(1, self.PkKills);
    }

    [Fact]
    public async Task Killing_a_flagged_player_is_a_pvp_kill_without_karma()
    {
        var (rig, a, b, w) = await TrioAsync();
        await using var _ = rig;
        b.Entity.PvpFlagged = true;
        b.Entity.PvpFlagUntilMs = rig.NowMs + ZoneWorld.PvpNormalTimeMs;
        b.Entity.CurHp = 1;

        await FightUntilAsync(rig, a, b, () => b.Entity.Dead);

        Assert.True(b.Entity.Dead);
        Assert.Equal(1, a.Entity.PvpKills);
        Assert.Equal(0, a.Entity.PkKills);
        Assert.Equal(0, a.Entity.Karma);
        Assert.False(b.Entity.PvpFlagged, "death clears the victim's flag");
        Assert.Equal(0, SpawnsOf(await w.DrainAsync(), b).Last().PvpFlag);
        Assert.Equal(1, UserInfos(await a.DrainAsync()).Last().PvpKills);
    }

    [Fact]
    public async Task Party_mates_need_ctrl_even_while_flagged()
    {
        var (rig, a, b, _) = await TrioAsync();
        await using var _r = rig;
        Assert.Null(await rig.World.ExecuteAsync(a.Entity.Session!, new PartyInviteCommand(b.ObjectId, 0), Ct));
        Assert.Null(await rig.World.ExecuteAsync(b.Entity.Session!, new PartyAnswerCommand(a.ObjectId, 1), Ct));
        b.Entity.PvpFlagged = true;
        b.Entity.PvpFlagUntilMs = rig.NowMs + ZoneWorld.PvpNormalTimeMs;

        await SendAttackAsync(a, b, forced: false);
        await a.DrainAsync();
        await rig.AdvanceAsync(2000);
        Assert.Equal(0, a.Entity.Intent.AutoAttackTargetId);
        Assert.Equal(100_000, b.Entity.CurHp);

        await SendAttackAsync(a, b, forced: true);
        await a.DrainAsync();
        await rig.AdvanceAsync(200);
        Assert.Equal(b.ObjectId, a.Entity.Intent.AutoAttackTargetId);
    }

    [Fact]
    public async Task Peace_zone_refuses_a_forced_swing_with_no_flag()
    {
        await using var rig = await NetMoveVisibilityRig.StartAsync(Ct);
        // The rig injects empty geo, which also skips the datapack zones: load the peace polygons explicitly.
        rig.World.Peace = Fineage.Zone.Geo.PeaceZones.LoadDefault();
        var a = await rig.JoinAsync(NetMoveVisibilityRig.X0, NetMoveVisibilityRig.Y0);
        var b = await rig.JoinAsync(NetMoveVisibilityRig.X0 + 30, NetMoveVisibilityRig.Y0);
        await a.DrainAsync();
        int hp = b.Entity.CurHp;

        await SendAttackAsync(a, b, forced: true);
        await a.DrainAsync();
        await rig.AdvanceAsync(3000);
        Assert.False(a.Entity.PvpFlagged);
        Assert.Equal(hp, b.Entity.CurHp);
    }

    [Fact]
    public async Task Mob_exp_burns_karma_and_the_name_turns_white_at_zero()
    {
        var (rig, a, _, w) = await TrioAsync();
        await using var _r = rig;
        a.Entity.Karma = 100;

        rig.World.GrantExpForTest(a.Entity, 30 * 40, 0); // 1200 exp / 30 = 40 karma
        Assert.Equal(60, a.Entity.Karma);
        Assert.Empty(SpawnsOf(await w.DrainAsync(), a));

        rig.World.GrantExpForTest(a.Entity, 30 * 1000, 0);
        Assert.Equal(0, a.Entity.Karma);
        Assert.Equal(0, SpawnsOf(await w.DrainAsync(), a).Last().Karma);
        Assert.Equal(0, rig.Store.FindById(a.Entity.Session!.Account, a.ObjectId)!.Karma);
    }
}
