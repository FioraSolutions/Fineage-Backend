using Fineage.Protocol;
using Fineage.Zone.Friends;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class FriendRulesTests
{
    [Fact]
    public void Invite_rejects_empty_self_missing_blocked_already_busy_and_limit()
    {
        Assert.Equal("bad_request", FriendRules.InviteError("", "A", true, false, false, false, 0, 0));
        Assert.Equal(FriendProtocol.Self, FriendRules.InviteError("A", "a", true, false, false, false, 0, 0));
        Assert.Equal(FriendProtocol.Missing, FriendRules.InviteError("B", "A", false, false, false, false, 0, 0));
        Assert.Equal(FriendProtocol.Blocked, FriendRules.InviteError("B", "A", true, true, false, false, 0, 0));
        Assert.Equal(FriendProtocol.Already, FriendRules.InviteError("B", "A", true, false, true, false, 0, 0));
        Assert.Equal(FriendProtocol.Busy, FriendRules.InviteError("B", "A", true, false, false, true, 0, 0));
        Assert.Equal(
            FriendProtocol.Limit,
            FriendRules.InviteError("B", "A", true, false, false, false, FriendProtocol.MaxFriends, 0));
        Assert.Null(FriendRules.InviteError("B", "A", true, false, false, false, 0, 0));
    }

    [Fact]
    public void Accept_rejects_missing_already_and_limit()
    {
        Assert.Equal(FriendProtocol.Missing, FriendRules.AcceptError(false, false, 0, 0));
        Assert.Equal(FriendProtocol.Already, FriendRules.AcceptError(true, true, 0, 0));
        Assert.Equal(
            FriendProtocol.Limit,
            FriendRules.AcceptError(true, false, FriendProtocol.MaxFriends, 0));
        Assert.Null(FriendRules.AcceptError(true, false, 0, 0));
    }

    [Fact]
    public void Block_add_rejects_empty_self_missing_already_and_limit()
    {
        Assert.Equal("bad_request", FriendRules.BlockAddError("", "A", 1, false, 0));
        Assert.Equal(FriendProtocol.Self, FriendRules.BlockAddError("A", "a", 1, false, 0));
        Assert.Equal(FriendProtocol.Missing, FriendRules.BlockAddError("B", "A", 0, false, 0));
        Assert.Equal(FriendProtocol.Already, FriendRules.BlockAddError("B", "A", 2, true, 0));
        Assert.Equal(
            FriendProtocol.Limit,
            FriendRules.BlockAddError("B", "A", 2, false, FriendProtocol.MaxBlocked));
        Assert.Null(FriendRules.BlockAddError("B", "A", 2, false, 0));
    }

    [Fact]
    public void Known_block_types_match_protocol()
    {
        Assert.True(FriendRules.IsKnownBlockType(FriendProtocol.BlockAdd));
        Assert.True(FriendRules.IsKnownBlockType(FriendProtocol.BlockRemove));
        Assert.True(FriendRules.IsKnownBlockType(FriendProtocol.BlockList));
        Assert.True(FriendRules.IsKnownBlockType(FriendProtocol.BlockAllOn));
        Assert.True(FriendRules.IsKnownBlockType(FriendProtocol.BlockAllOff));
        Assert.False(FriendRules.IsKnownBlockType(99));
    }
}
