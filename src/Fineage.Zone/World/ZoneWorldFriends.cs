using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Friends;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius friends + block list (#285): invite / answer / delete / list, block / unblock / block-all.
/// </summary>
public sealed partial class ZoneWorld
{
    readonly FriendsActor _friends = new();

    /// <summary>The friend and block lists of a player in the world.</summary>
    SocialLists Lists(ZoneEntity player) => _friends.Of(player.ObjectId);

    /// <summary>Test hook: the live lists of a player in the world.</summary>
    internal SocialLists SocialListsForTests(ZoneEntity player)
    {
        using (_affinity.Step())
            return Lists(player);
    }

    public async Task<string?> TryFriendInviteAsync(ZoneSession session, FriendInviteMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error = TryFriendInvite(session, request);
        return error;
    }

    public async Task<string?> TryFriendAnswerAsync(ZoneSession session, FriendAnswerMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error = TryFriendAnswer(session, request);
        return error;
    }

    public async Task<string?> TryFriendListAsync(ZoneSession session, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";
        string? error = TryFriendList(session);
        return error;
    }

    public async Task<string?> TryFriendDeleteAsync(ZoneSession session, FriendDeleteMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error = TryFriendDelete(session, request);
        return error;
    }

    public async Task<string?> TryBlockRequestAsync(ZoneSession session, BlockRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error = TryBlockRequest(session, request);
        return error;
    }

    void LoadSocialLists(ZoneEntity entity, CharacterRecord? stored) => _friends.Load(entity.ObjectId, entity.Name, stored);

    void FriendPlayerEnteredWorld(ZoneEntity player)
    {
        foreach (int friendId in _friends.MutualOnline(player.ObjectId))
        {
            if (!_byId.TryGetValue(friendId, out var friend))
                continue;
            FanoutFriendUpdate(friend.ObjectId, added: true, player.ObjectId, player.Name, online: true);
            FanoutFriendUpdate(player.ObjectId, added: true, friend.ObjectId, friend.Name, online: true);
        }
    }

    /// <summary>Tells the leaver's online friends it went offline, then drops its lists and invites.</summary>
    void FriendPlayerLeftWorld(int objectId)
    {
        if (!_friends.TryGet(objectId, out var leaving))
            return;
        foreach (int friendId in _friends.MutualOnline(objectId))
            FanoutFriendUpdate(friendId, added: true, objectId, leaving.Name, online: false);
        _friends.Unload(objectId);
    }

    string? TryFriendInvite(ZoneSession session, FriendInviteMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";
        string name = (request.Name ?? "").Trim();
        var target = FindOnlinePlayerByName(name);
        bool targetOnline = target?.Session != null && target.Session.InWorld;
        bool blockedEitherWay = targetOnline
            && (IsBlocked(target!, self) || Lists(self).BlockAll || IsBlocked(self, target!) || Lists(target!).BlockAll);
        bool alreadyFriends = targetOnline && Lists(self).Friends.ContainsKey(target!.ObjectId);
        bool eitherBusy = targetOnline
            && (_friends.IsInvited(target!.ObjectId) || _friends.IsInvited(self.ObjectId));
        if (FriendRules.InviteError(
                name,
                self.Name,
                targetOnline,
                blockedEitherWay,
                alreadyFriends,
                eitherBusy,
                Lists(self).Friends.Count,
                targetOnline ? Lists(target!).Friends.Count : 0) is { } inviteError)
            return inviteError;

        _friends.Invite(target!.ObjectId, self.ObjectId);
        NetBridge?.FanoutFriendInviteAsk([target.ObjectId], new FriendInviteAskMsg
        {
            RequestorName = self.Name
        });
        NotifyLine(self.ObjectId, 0, $"You requested {target.Name} to be on your Friends List.");
        return null;
    }

    string? TryFriendAnswer(ZoneSession session, FriendAnswerMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";
        if (!_friends.TakeInvite(self.ObjectId, out int inviterId))
            return FriendProtocol.Busy;
        if (!_byId.TryGetValue(inviterId, out var inviter) || inviter.Session == null || !inviter.Session.InWorld)
            return FriendProtocol.Missing;

        if (request.Response != 1)
        {
            NotifyLine(inviter.ObjectId, 0, "You have failed to add a friend to your friends list.");
            return null;
        }

        if (FriendRules.AcceptError(
                inviterOnlineInWorld: true,
                alreadyFriendsEitherWay: Lists(self).Friends.ContainsKey(inviter.ObjectId)
                    || Lists(inviter).Friends.ContainsKey(self.ObjectId),
                selfFriendCount: Lists(self).Friends.Count,
                inviterFriendCount: Lists(inviter).Friends.Count) is { } acceptError)
            return acceptError;

        Lists(self).Friends[inviter.ObjectId] = inviter.Name;
        Lists(inviter).Friends[self.ObjectId] = self.Name;
        PersistFriends(self);
        PersistFriends(inviter);

        FanoutFriendUpdate(self.ObjectId, added: true, inviter.ObjectId, inviter.Name, online: true);
        FanoutFriendUpdate(inviter.ObjectId, added: true, self.ObjectId, self.Name, online: true);
        NotifyLine(self.ObjectId, 0, $"{inviter.Name} has joined as a friend.");
        NotifyLine(inviter.ObjectId, 0, $"{self.Name} has been added to your Friends List.");
        return null;
    }

    string? TryFriendList(ZoneSession session)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";
        var rows = Lists(self).Friends
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new FriendRowMsg
            {
                ObjectId = kv.Key,
                Name = LiveNameOr(kv.Key, kv.Value),
                Online = IsOnlinePlayer(kv.Key) ? 1 : 0
            })
            .ToArray();
        NetBridge?.FanoutFriendList([self.ObjectId], new FriendListMsg { Friends = rows });
        return null;
    }

}
