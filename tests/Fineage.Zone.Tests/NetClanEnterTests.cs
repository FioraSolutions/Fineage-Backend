using Fineage.Net.Contract;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>EnterWorld</c> boot sequence for a returning clan member: the pledge sheet and the roster are part of
/// the enter burst, so the clan window is filled before the player can open it. The world builds both from its clan
/// cache; the Net server writes them right after the journal.
/// </summary>
public sealed class NetClanEnterTests
{
    [Fact]
    public async Task A_clan_member_entering_gets_the_pledge_sheet_and_the_roster()
    {
        var kit = new NetTestKit();
        var leader = kit.Create(level: ClanProtocol.MinCreateLevel);
        var member = kit.Create(level: ClanProtocol.MinCreateLevel);

        var (leaderSession, leaderEntity) = await kit.EnterAsync(leader);
        Assert.Null(await kit.World.TryClanCreateAsync(
            leaderSession, new ClanCreateRequestMsg { Name = "Fine" }, kit.Now, default));

        var (memberSession, memberEntity) = await kit.EnterAsync(member);
        Assert.Null(await kit.World.TryClanInviteAsync(
            leaderSession, new ClanInviteRequestMsg { TargetObjectId = memberEntity.ObjectId }, kit.Now, default));
        Assert.Null(await kit.World.TryClanAnswerAsync(
            memberSession, new ClanAnswerJoinRequestMsg { RequestorObjectId = leaderEntity.ObjectId, Response = 1 },
            kit.Now, default));

        // What the Net enter path reads: a clan member gets both rows, an outsider gets neither.
        Assert.True(kit.World.TryBuildNetClanSnapshot(memberSession, out var status, out var roster));
        Assert.Equal("Fine", status.Name);
        Assert.Equal(2, status.MemberCount);
        Assert.Equal(2, roster.Members.Length);
        Assert.Contains(roster.Members, row => row.ObjectId == memberEntity.ObjectId && row.Online);

        var (outsider, _) = await kit.EnterAsync();
        Assert.False(kit.World.TryBuildNetClanSnapshot(outsider, out _, out _));
    }
}
