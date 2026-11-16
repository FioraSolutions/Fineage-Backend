using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Interlude party window feeds: PartySmallWindowUpdate (bars), PartySpelled (member effects) and
/// PartyMemberPosition (map dots, AOI-independent), plus trade teardown when the pair drifts apart.
/// </summary>
public sealed class NetPartyStatusTests
{
    static (NetTestKit Kit, NetBridgeRecorder Rec) World()
    {
        var kit = new NetTestKit(aoi: new Fineage.Zone.Interest.RangeAoi(2000));
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(ZoneSession Leader, ZoneEntity LeaderEntity, ZoneSession Member, ZoneEntity MemberEntity)> Party(
        NetTestKit kit, int memberX = NetTestKit.ElfX)
    {
        var (leader, leaderEntity) = await kit.EnterAsync();
        var (member, memberEntity) = await kit.EnterAsync(x: memberX);
        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leader.ObjectId, 1, default));
        return (leader, leaderEntity, member, memberEntity);
    }

    static async Task Tick(NetTestKit kit, int ms)
    {
        kit.Now = kit.Now.AddMilliseconds(ms);
        await kit.World.RunPartyTickForTest();
    }

    static List<(int[] To, PartyMemberUpdateMsg Msg)> Updates(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberUpdate))
            .Select(c => (Assert.IsAssignableFrom<IReadOnlyList<int>>(c[0]).ToArray(), Assert.IsType<PartyMemberUpdateMsg>(c[1])))
            .ToList();

    [Fact]
    public async Task Hp_change_sends_one_coalesced_bar_update_to_the_other_members_only()
    {
        var (kit, rec) = World();
        var (_, leaderEntity, _, memberEntity) = await Party(kit);
        await Tick(kit, 1000);   // join: first bars snapshot for everyone
        int listsBefore = rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList)).Count;
        int updatesBefore = Updates(rec).Count;

        kit.World.SetPlayerHpForTest(memberEntity, memberEntity.MaxHp - 10);
        kit.World.SetPlayerHpForTest(memberEntity, memberEntity.MaxHp - 20);
        kit.World.SetPlayerHpForTest(memberEntity, memberEntity.MaxHp - 30);
        await Tick(kit, 1000);

        var fresh = Updates(rec).Skip(updatesBefore).ToList();
        var only = Assert.Single(fresh);
        Assert.Equal([leaderEntity.ObjectId], only.To);
        Assert.Equal(memberEntity.ObjectId, only.Msg.ObjectId);
        Assert.Equal(memberEntity.MaxHp - 30, only.Msg.CurHp);
        Assert.Equal(memberEntity.MaxHp, only.Msg.MaxHp);
        Assert.Equal(memberEntity.CurMp, only.Msg.CurMp);
        Assert.Equal(memberEntity.Level, only.Msg.Level);
        Assert.Equal(memberEntity.ClassId, only.Msg.ClassId);
        Assert.True(only.Msg.MaxCp > 0);
        Assert.Equal(only.Msg.MaxCp, only.Msg.CurCp);
        Assert.Equal(listsBefore, rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList)).Count);

        // Inside the window a new change waits; an unchanged value is never resent.
        kit.World.SetPlayerHpForTest(memberEntity, memberEntity.MaxHp - 40);
        await Tick(kit, PartyUpdateIntervalMs() / 3);
        Assert.Equal(updatesBefore + 1, Updates(rec).Count);
        await Tick(kit, PartyUpdateIntervalMs());
        Assert.Equal(memberEntity.MaxHp - 40, Updates(rec).Last().Msg.CurHp);
        int sent = Updates(rec).Count;
        kit.World.SetPlayerHpForTest(memberEntity, memberEntity.MaxHp - 40);
        await Tick(kit, 1000);
        Assert.Equal(sent, Updates(rec).Count);
    }

    static int PartyUpdateIntervalMs() => 300;

    [Fact]
    public async Task Party_list_rows_carry_cp_and_survive_the_wire_with_an_old_style_body()
    {
        var (kit, rec) = World();
        await Party(kit);
        var list = Assert.IsType<PartyMemberListMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutPartyMemberList))[^1][1]);
        Assert.All(list.Members, m => Assert.True(m.MaxCp > 0 && m.CurCp == m.MaxCp));

        var wire = NetCodec.EncodePartyMemberList(new PartyMemberListNetMsg
        {
            LeaderObjectId = 1, PartyId = 2,
            Members = [new PartyMemberEntryNet { ObjectId = 1, Name = "A", CurHp = 5, MaxHp = 9, CurCp = 33, MaxCp = 44 }]
        });
        var back = NetCodec.DecodePartyMemberList(wire);
        Assert.Equal((33, 44), (back.Members[0].CurCp, back.Members[0].MaxCp));
        // A pre-CP body (no tail) still decodes with CP = 0.
        var legacy = wire[..^12];
        var old = NetCodec.DecodePartyMemberList(legacy);
        Assert.Equal((5, 9, 0), (old.Members[0].CurHp, old.Members[0].MaxHp, old.Members[0].MaxCp));

        var upd = NetCodec.DecodePartyMemberUpdate(NetCodec.EncodePartyMemberUpdate(
            new PartyMemberUpdateNetMsg { ObjectId = 7, Level = 20, ClassId = 4, CurHp = 1, MaxHp = 2, CurMp = 3, MaxMp = 4, CurCp = 5, MaxCp = 6 }));
        Assert.Equal((7, 20, 4, 1, 2, 3, 4, 5, 6), (upd.ObjectId, upd.Level, upd.ClassId, upd.CurHp, upd.MaxHp, upd.CurMp, upd.MaxMp, upd.CurCp, upd.MaxCp));
    }

    [Fact]
    public async Task Member_effects_reach_the_party_on_join_apply_and_expire()
    {
        var (kit, rec) = World();
        var (leader, leaderEntity) = await kit.EnterAsync();
        var (member, memberEntity) = await kit.EnterAsync();
        var template = kit.Game.Skills.Find(1062);   // Berserker Spirit
        Assert.NotNull(template);
        Assert.True(template!.TryLevelData(1, out var level));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, level, memberEntity));
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutPartySpelled)));   // not in a party yet

        Assert.Null(await kit.World.TryPartyInviteAsync(leader, memberEntity.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(member, leader.ObjectId, 1, default));
        var onJoin = rec.Of(nameof(IWorldEventPublisher.FanoutPartySpelled))
            .Select(c => (To: Assert.IsAssignableFrom<IReadOnlyList<int>>(c[0]), Msg: Assert.IsType<AbnormalStatusUpdateMsg>(c[1])))
            .ToList();
        var memberToLeader = Assert.Single(onJoin, x => x.Msg.ObjectId == memberEntity.ObjectId);
        Assert.Equal([leaderEntity.ObjectId], memberToLeader.To);
        var effect = Assert.Single(memberToLeader.Msg.Effects);
        Assert.Equal(1062, effect.Id);
        Assert.Equal(1, effect.Value);
        Assert.True(effect.DurationSeconds > 0);
        Assert.Contains(onJoin, x => x.Msg.ObjectId == leaderEntity.ObjectId && x.To.SequenceEqual([memberEntity.ObjectId]));

        // Apply on the leader → member sees it; expiry → member gets the emptied list.
        int before = rec.Of(nameof(IWorldEventPublisher.FanoutPartySpelled)).Count;
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, level, leaderEntity));
        var applied = rec.Of(nameof(IWorldEventPublisher.FanoutPartySpelled))[before];
        Assert.Equal([memberEntity.ObjectId], Assert.IsAssignableFrom<IReadOnlyList<int>>(applied[0]));
        Assert.Contains(Assert.IsType<AbnormalStatusUpdateMsg>(applied[1]).Effects, e => e.Id == 1062);

        kit.Now = kit.Now.AddSeconds(template.AbnormalTimeSeconds + 5);
        await kit.World.RunAbnormalTickAsync(default);
        var expired = rec.Of(nameof(IWorldEventPublisher.FanoutPartySpelled))
            .Select(c => Assert.IsType<AbnormalStatusUpdateMsg>(c[1]))
            .Last(m => m.ObjectId == leaderEntity.ObjectId);
        Assert.DoesNotContain(expired.Effects, e => e.Id == 1062);
    }

    [Fact]
    public async Task Party_positions_reach_members_outside_aoi_periodically_and_only_when_changed()
    {
        var (kit, rec) = World();
        // 20 000 units apart: far outside any knownlist.
        var (leader, leaderEntity, member, memberEntity) = await Party(kit, memberX: NetTestKit.ElfX + 20_000);
        Assert.DoesNotContain(memberEntity.ObjectId, leaderEntity.Knownlist);
        string pos = nameof(IWorldEventPublisher.FanoutPartyMemberPosition);

        await Tick(kit, 100);
        var first = rec.Of(pos).Select(c => (To: Assert.IsAssignableFrom<IReadOnlyList<int>>(c[0]).Single(), Msg: Assert.IsType<PartyMemberPositionMsg>(c[1]))).ToList();
        Assert.Equal(2, first.Count);
        var toLeader = Assert.Single(first, x => x.To == leaderEntity.ObjectId).Msg;
        var row = Assert.Single(toLeader.Members);   // never your own dot
        Assert.Equal((memberEntity.ObjectId, memberEntity.X, memberEntity.Y), (row.ObjectId, row.X, row.Y));

        // Nobody moved: the next periods are silent; before the period nothing is sent even after a move.
        await Tick(kit, 3100);
        Assert.Equal(2, rec.Of(pos).Count);
        int startX = memberEntity.X;
        Assert.Null(await kit.World.TryMoveAsync(member, memberEntity.X + 400, memberEntity.Y, memberEntity.Z, default));
        kit.Now = kit.Now.AddMilliseconds(500);
        await kit.World.RunPlayerCombatTickAsync(default);
        Assert.Equal(2, rec.Of(pos).Count);

        // Only the mover's row, only to the other member.
        kit.World.StopMove(member);
        Assert.True(memberEntity.X > startX);
        await Tick(kit, 3000);
        var moved = rec.Of(pos).Skip(2).ToList();
        var call = Assert.Single(moved);
        Assert.Equal([leaderEntity.ObjectId], Assert.IsAssignableFrom<IReadOnlyList<int>>(call[0]));
        var r = Assert.Single(Assert.IsType<PartyMemberPositionMsg>(call[1]).Members);
        Assert.Equal((memberEntity.ObjectId, memberEntity.X), (r.ObjectId, r.X));

        var wire = NetCodec.DecodePartyMemberPosition(NetCodec.EncodePartyMemberPosition(new PartyMemberPositionNetMsg
        {
            Members = [new PartyMemberPositionEntryNet { ObjectId = 3, X = -5, Y = 6, Z = -7 }]
        }));
        Assert.Equal((3, -5, 6, -7), (wire.Members[0].ObjectId, wire.Members[0].X, wire.Members[0].Y, wire.Members[0].Z));
        _ = leader;
    }

    [Fact]
    public async Task Walking_out_of_trade_range_closes_the_window_for_both_sides()
    {
        var (kit, rec) = World();
        var (a, aEntity) = await kit.EnterAsync();
        var (b, bEntity) = await kit.EnterAsync(x: NetTestKit.ElfX + 50);
        Assert.Null(await kit.World.TryTradeRequestAsync(a, new TradeRequestMsg { TargetObjectId = bEntity.ObjectId }, default));
        Assert.Null(await kit.World.TryTradeAnswerAsync(b, new TradeAnswerRequestMsg { RequestorObjectId = aEntity.ObjectId, Response = 1 }, default));

        await kit.World.RunTradeRangeTickForTest();
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutTradeFinished)));

        Assert.Null(await kit.World.TryMoveAsync(b, bEntity.X + 1000, bEntity.Y, bEntity.Z, default));
        kit.Now = kit.Now.AddSeconds(3);
        await kit.World.RunPlayerCombatTickAsync(default);

        var finished = rec.Of(nameof(IWorldEventPublisher.FanoutTradeFinished));
        Assert.Equal(2, finished.Count);
        Assert.All(finished, c => Assert.Equal(0, Assert.IsType<TradeFinishedMsg>(c[1]).Response));
        Assert.NotNull(await kit.World.TryTradeDoneAsync(a, new TradeDoneRequestMsg { Response = 1 }, default));
    }
}
