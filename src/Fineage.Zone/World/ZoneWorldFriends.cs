using Fineage.Login;
using Fineage.Protocol;

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
        if (name.Length == 0)
            return "bad_request";
        if (string.Equals(self.Name, name, StringComparison.OrdinalIgnoreCase))
            return FriendProtocol.Self;

        var target = FindOnlinePlayerByName(name);
        if (target == null || target.Session == null || !target.Session.InWorld)
            return FriendProtocol.Missing;
        if (IsBlocked(target, self) || self.BlockAll)
            return FriendProtocol.Blocked;
        if (IsBlocked(self, target) || target.BlockAll)
            return FriendProtocol.Blocked;
        if (self.Friends.ContainsKey(target.ObjectId))
            return FriendProtocol.Already;
        if (_pendingFriendInvites.ContainsKey(target.ObjectId) || _pendingFriendInvites.ContainsKey(self.ObjectId))
            return FriendProtocol.Busy;
        if (self.Friends.Count >= FriendProtocol.MaxFriends || target.Friends.Count >= FriendProtocol.MaxFriends)
            return FriendProtocol.Limit;

        _pendingFriendInvites[target.ObjectId] = self.ObjectId;
        NetBridge?.FanoutFriendInviteAsk([target.ObjectId], new Fineage.Net.Contract.FriendInviteAskNetMsg
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

        if (self.Friends.ContainsKey(inviter.ObjectId) || inviter.Friends.ContainsKey(self.ObjectId))
            return FriendProtocol.Already;
        if (self.Friends.Count >= FriendProtocol.MaxFriends || inviter.Friends.Count >= FriendProtocol.MaxFriends)
            return FriendProtocol.Limit;

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
            .Select(kv => new Fineage.Net.Contract.FriendRowNet
            {
                ObjectId = kv.Key,
                Name = LiveNameOr(kv.Key, kv.Value),
                Online = IsOnlinePlayer(kv.Key) ? 1 : 0
            })
            .ToArray();
        NetBridge?.FanoutFriendList([self.ObjectId], new Fineage.Net.Contract.FriendListNetMsg { Friends = rows });
        return null;
    }

    string? TryFriendDelete(ZoneSession session, FriendDeleteMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";
        string name = (request.Name ?? "").Trim();
        if (name.Length == 0)
            return "bad_request";

        int friendId = self.Friends
            .FirstOrDefault(kv => string.Equals(kv.Value, name, StringComparison.OrdinalIgnoreCase))
            .Key;
        if (friendId == 0)
        {
            // Name may have changed — try live / store lookup.
            var online = FindOnlinePlayerByName(name);
            if (online != null && self.Friends.ContainsKey(online.ObjectId))
                friendId = online.ObjectId;
            else if (_accounts?.FindByCharacterName(name) is { } stored && self.Friends.ContainsKey(stored.Id))
                friendId = stored.Id;
        }
        if (friendId == 0 || !self.Friends.Remove(friendId, out string? friendName))
            return FriendProtocol.NotFriend;

        PersistFriends(self);
        FanoutFriendUpdate(self.ObjectId, added: false, friendId, friendName, online: 0);

        if (_byId.TryGetValue(friendId, out var peer) && peer.Kind == ZoneEntityKind.Player)
        {
            if (peer.Friends.Remove(self.ObjectId))
            {
                PersistFriends(peer);
                if (peer.Session != null && peer.Session.InWorld)
                    FanoutFriendUpdate(peer.ObjectId, added: false, self.ObjectId, self.Name, online: 0);
            }
        }
        else if (_accounts?.FindByCharacterId(friendId) is { } offline)
        {
            var remaining = (offline.Friends ?? [])
                .Where(f => f.ObjectId != self.ObjectId)
                .ToArray();
            _accounts.TryReplaceFriends(offline.Account, offline.Id, remaining);
        }

        NotifyLine(self.ObjectId, 0, $"{friendName} has been deleted from your Friends List.");
        return null;
    }

    string? TryBlockRequest(ZoneSession session, BlockRequestMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var self))
            return "world";

        switch (request.Type)
        {
            case FriendProtocol.BlockList:
                SendBlockList(self);
                return null;
            case FriendProtocol.BlockAllOn:
                self.BlockAll = true;
                PersistBlocked(self);
                NotifyLine(self.ObjectId, 0, "Message refusal mode.");
                return null;
            case FriendProtocol.BlockAllOff:
                self.BlockAll = false;
                PersistBlocked(self);
                NotifyLine(self.ObjectId, 0, "Message acceptance mode.");
                return null;
            case FriendProtocol.BlockAdd:
            case FriendProtocol.BlockRemove:
                break;
            default:
                return "bad_request";
        }

        string name = (request.Name ?? "").Trim();
        if (name.Length == 0)
            return "bad_request";
        if (string.Equals(self.Name, name, StringComparison.OrdinalIgnoreCase))
            return FriendProtocol.Self;

        int targetId = 0;
        string targetName = name;
        var online = FindOnlinePlayerByName(name);
        if (online != null)
        {
            targetId = online.ObjectId;
            targetName = online.Name;
        }
        else if (_accounts?.FindByCharacterName(name) is { } stored)
        {
            targetId = stored.Id;
            targetName = stored.Name;
        }
        if (targetId < 1)
            return FriendProtocol.Missing;

        if (request.Type == FriendProtocol.BlockAdd)
        {
            if (self.Blocked.ContainsKey(targetId))
                return FriendProtocol.Already;
            if (self.Blocked.Count >= FriendProtocol.MaxBlocked)
                return FriendProtocol.Limit;
            self.Blocked[targetId] = targetName;
            // Mobius: blocking removes from friends both ways when present.
            if (self.Friends.Remove(targetId))
            {
                PersistFriends(self);
                FanoutFriendUpdate(self.ObjectId, added: false, targetId, targetName, online: 0);
                if (_byId.TryGetValue(targetId, out var peer) && peer.Friends.Remove(self.ObjectId))
                {
                    PersistFriends(peer);
                    if (peer.Session?.InWorld == true)
                        FanoutFriendUpdate(peer.ObjectId, added: false, self.ObjectId, self.Name, online: 0);
                }
            }
            PersistBlocked(self);
            NotifyLine(self.ObjectId, 0, $"You have blocked {targetName}.");
            return null;
        }

        if (!self.Blocked.Remove(targetId))
            return FriendProtocol.Missing;
        PersistBlocked(self);
        NotifyLine(self.ObjectId, 0, $"You have unblocked {targetName}.");
        return null;
    }

    void SendBlockList(ZoneEntity self)
    {
        var rows = self.Blocked
            .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new Fineage.Net.Contract.FriendRowNet
            {
                ObjectId = kv.Key,
                Name = kv.Value,
                Online = IsOnlinePlayer(kv.Key) ? 1 : 0
            })
            .ToArray();
        NetBridge?.FanoutBlockList([self.ObjectId], new Fineage.Net.Contract.BlockListNetMsg
        {
            BlockAll = self.BlockAll ? 1 : 0,
            Blocked = rows
        });
    }

    void PersistFriends(ZoneEntity player)
    {
        if (_accounts == null || player.Session == null)
            return;
        var rows = player.Friends
            .OrderBy(kv => kv.Key)
            .Select(kv => new StoredSocial(kv.Key, kv.Value))
            .ToArray();
        _accounts.TryReplaceFriends(player.Session.Account, player.ObjectId, rows);
    }

    void PersistBlocked(ZoneEntity player)
    {
        if (_accounts == null || player.Session == null)
            return;
        var rows = player.Blocked
            .OrderBy(kv => kv.Key)
            .Select(kv => new StoredSocial(kv.Key, kv.Value))
            .ToArray();
        _accounts.TryReplaceBlocked(player.Session.Account, player.ObjectId, rows, player.BlockAll);
    }

    ZoneEntity? FindOnlinePlayerByName(string name)
    {
        foreach (var player in Players())
        {
            if (player.Session != null
                && player.Session.InWorld
                && string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase))
                return player;
        }
        return null;
    }

    bool IsOnlinePlayer(int objectId) =>
        _byId.TryGetValue(objectId, out var entity)
        && entity.Kind == ZoneEntityKind.Player
        && entity.Session != null
        && entity.Session.InWorld;

    string LiveNameOr(int objectId, string fallback) =>
        _byId.TryGetValue(objectId, out var entity) && entity.Kind == ZoneEntityKind.Player
            ? entity.Name
            : fallback;

    static bool IsBlocked(ZoneEntity owner, ZoneEntity other) =>
        owner.BlockAll || owner.Blocked.ContainsKey(other.ObjectId);

    void FanoutFriendUpdate(int watcherObjectId, bool added, int objectId, string name, bool online) =>
        FanoutFriendUpdate(watcherObjectId, added, objectId, name, online ? 1 : 0);

    void FanoutFriendUpdate(int watcherObjectId, bool added, int objectId, string name, int online)
    {
        NetBridge?.FanoutFriendUpdate([watcherObjectId], new Fineage.Net.Contract.FriendUpdateNetMsg
        {
            Added = added ? 1 : 0,
            ObjectId = objectId,
            Name = name,
            Online = online
        });
    }
}
