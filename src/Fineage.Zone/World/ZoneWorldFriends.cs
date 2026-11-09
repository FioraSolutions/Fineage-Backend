using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Friends;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius friends + block list (#285): invite / answer / delete / list, block / unblock / block-all.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Invitee objectId → inviter objectId.</summary>
    readonly Dictionary<int, int> _pendingFriendInvites = [];

    public async Task<string?> TryFriendInviteAsync(ZoneSession session, FriendInviteMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryFriendInvite(session, request);
        return error;
    }

    public async Task<string?> TryFriendAnswerAsync(ZoneSession session, FriendAnswerMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryFriendAnswer(session, request);
        return error;
    }

    public async Task<string?> TryFriendListAsync(ZoneSession session, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryFriendList(session);
        return error;
    }

    public async Task<string?> TryFriendDeleteAsync(ZoneSession session, FriendDeleteMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryFriendDelete(session, request);
        return error;
    }

    public async Task<string?> TryBlockRequestAsync(ZoneSession session, BlockRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!session.InWorld)
            return "world";
        string? error;
        lock (_gate)
            error = TryBlockRequest(session, request);
        return error;
    }

    void LoadSocialLists(ZoneEntity entity, CharacterRecord? stored)
    {
        entity.Friends.Clear();
        entity.Blocked.Clear();
        entity.BlockAll = stored?.BlockAll ?? false;
        if (stored?.Friends != null)
        {
            foreach (var row in stored.Friends)
            {
                if (row.ObjectId > 0 && !string.IsNullOrWhiteSpace(row.Name))
                    entity.Friends[row.ObjectId] = row.Name.Trim();
            }
        }
        if (stored?.Blocked != null)
        {
            foreach (var row in stored.Blocked)
            {
                if (row.ObjectId > 0 && !string.IsNullOrWhiteSpace(row.Name))
                    entity.Blocked[row.ObjectId] = row.Name.Trim();
            }
        }
    }

    void FriendPlayerEnteredWorld(ZoneEntity player)
    {
        foreach (var (friendId, _) in player.Friends)
        {
            if (!_byId.TryGetValue(friendId, out var friend) || friend.Session == null || !friend.Session.InWorld)
                continue;
            if (!friend.Friends.ContainsKey(player.ObjectId))
                continue;
            FanoutFriendUpdate(friend.ObjectId, added: true, player.ObjectId, player.Name, online: true);
            FanoutFriendUpdate(player.ObjectId, added: true, friend.ObjectId, friend.Name, online: true);
        }
    }

    void FriendPlayerLeftWorld(int objectId)
    {
        _pendingFriendInvites.Remove(objectId);
        foreach (var (inviteeId, inviterId) in _pendingFriendInvites.ToList())
        {
            if (inviterId == objectId)
                _pendingFriendInvites.Remove(inviteeId);
        }

        if (!_byId.TryGetValue(objectId, out var leaving) || leaving.Kind != ZoneEntityKind.Player)
            return;
        foreach (var (friendId, _) in leaving.Friends)
        {
            if (!_byId.TryGetValue(friendId, out var friend) || friend.Session == null || !friend.Session.InWorld)
                continue;
            if (!friend.Friends.ContainsKey(objectId))
                continue;
            FanoutFriendUpdate(friend.ObjectId, added: true, objectId, leaving.Name, online: false);
        }
    }

    string? TryFriendInvite(ZoneSession session, FriendInviteMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";
        string name = (request.Name ?? "").Trim();
        var target = FindOnlinePlayerByName(name);
        bool targetOnline = target?.Session != null && target.Session.InWorld;
        bool blockedEitherWay = targetOnline
            && (IsBlocked(target!, self) || self.BlockAll || IsBlocked(self, target!) || target!.BlockAll);
        bool alreadyFriends = targetOnline && self.Friends.ContainsKey(target!.ObjectId);
        bool eitherBusy = targetOnline
            && (_pendingFriendInvites.ContainsKey(target!.ObjectId) || _pendingFriendInvites.ContainsKey(self.ObjectId));
        if (FriendRules.InviteError(
                name,
                self.Name,
                targetOnline,
                blockedEitherWay,
                alreadyFriends,
                eitherBusy,
                self.Friends.Count,
                targetOnline ? target!.Friends.Count : 0) is { } inviteError)
            return inviteError;

        _pendingFriendInvites[target!.ObjectId] = self.ObjectId;
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
        if (!_pendingFriendInvites.Remove(self.ObjectId, out int inviterId))
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
                alreadyFriendsEitherWay: self.Friends.ContainsKey(inviter.ObjectId)
                    || inviter.Friends.ContainsKey(self.ObjectId),
                selfFriendCount: self.Friends.Count,
                inviterFriendCount: inviter.Friends.Count) is { } acceptError)
            return acceptError;

        self.Friends[inviter.ObjectId] = inviter.Name;
        inviter.Friends[self.ObjectId] = self.Name;
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
        var rows = self.Friends
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
